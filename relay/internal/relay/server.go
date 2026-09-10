package relay

import (
	"crypto/sha256"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"io"
	"io/fs"
	"mime"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/gorilla/websocket"
)

const (
	protocolVersion      = 1
	maximumFrameBytes    = 64 * 1024
	maximumRegisterBytes = 2 * 1024
	registrationTimeout  = 10 * time.Second
	writeTimeout         = 5 * time.Second
	readPongTimeout      = 60 * time.Second
	heartbeatInterval    = 20 * time.Second
	ratePerSecond        = 10.0
	rateBurst            = 20.0
)

var (
	channelPattern     = regexp.MustCompile(`^[A-Za-z0-9_-]{22}$`)
	tokenPattern       = regexp.MustCompile(`^[A-Za-z0-9_-]{43}$`)
	releaseTagPattern  = regexp.MustCompile(`^v?[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?$`)
	updateAssetPattern = regexp.MustCompile(`^BetterGI\.Remote\.Setup\.[0-9]+\.[0-9]+\.[0-9]+\.exe$`)
)

type Options struct {
	AllowedOrigin      string
	MaxConnections     int
	Now                func() time.Time
	HTTPClient         *http.Client
	BetterGiReleaseURL string
	UpdateRoot         string
}

type Server struct {
	options             Options
	rooms               map[string]*room
	roomsMu             sync.Mutex
	connections         atomic.Int64
	upgrader            websocket.Upgrader
	releaseClient       *http.Client
	betterGiReleaseURL  string
	betterGiMu          sync.Mutex
	betterGiCached      []byte
	betterGiCachedUntil time.Time
}

type registration struct {
	Type            string `json:"type"`
	ProtocolVersion int    `json:"protocolVersion"`
	ChannelID       string `json:"channelId"`
	Role            string `json:"role"`
	RelayToken      string `json:"relayToken"`
}

type controlMessage struct {
	Type     string `json:"type"`
	Code     string `json:"code,omitempty"`
	Online   *bool  `json:"online,omitempty"`
	Revision uint64 `json:"revision,omitempty"`
}

type room struct {
	channelID string
	tokenHash [32]byte
	pc        *client
	phone     *client
	revision  uint64
}

type client struct {
	conn   *websocket.Conn
	server *Server
	room   *room
	role   string
	write  sync.Mutex
	limit  tokenBucket
}

type tokenBucket struct {
	mu       sync.Mutex
	tokens   float64
	lastFill time.Time
}

func NewServer(options Options) *Server {
	if options.MaxConnections <= 0 {
		options.MaxConnections = 200
	}
	if options.Now == nil {
		options.Now = time.Now
	}
	if options.HTTPClient == nil {
		options.HTTPClient = &http.Client{Timeout: 15 * time.Second}
	}
	if options.BetterGiReleaseURL == "" {
		options.BetterGiReleaseURL = "https://api.github.com/repos/babalae/better-genshin-impact/releases/latest"
	}

	server := &Server{
		options:            options,
		rooms:              make(map[string]*room),
		releaseClient:      options.HTTPClient,
		betterGiReleaseURL: options.BetterGiReleaseURL,
	}
	server.upgrader = websocket.Upgrader{
		ReadBufferSize:  1024,
		WriteBufferSize: 1024,
		CheckOrigin:     server.checkOrigin,
	}
	return server
}

func (s *Server) Handler(webRoot string) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/healthz", s.handleHealth)
	mux.HandleFunc("/api/bettergi/latest", s.handleBetterGiRelease)
	mux.HandleFunc("/ws", s.handleWebSocket)
	if s.options.UpdateRoot != "" {
		mux.HandleFunc("/updates/latest.json", s.handleUpdateManifest)
		mux.HandleFunc("/downloads/", s.handleUpdateDownload)
	}

	if webRoot != "" {
		mux.Handle("/", spaHandler(webRoot))
	} else {
		mux.HandleFunc("/", func(w http.ResponseWriter, _ *http.Request) {
			http.Error(w, "BetterGI Remote Lite relay", http.StatusNotFound)
		})
	}
	return securityHeaders(mux)
}

func (s *Server) handleUpdateManifest(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet && r.Method != http.MethodHead {
		w.Header().Set("Allow", "GET, HEAD")
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-cache, no-store, must-revalidate")
	http.ServeFile(w, r, filepath.Join(s.options.UpdateRoot, "latest.json"))
}

func (s *Server) handleUpdateDownload(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet && r.Method != http.MethodHead {
		w.Header().Set("Allow", "GET, HEAD")
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	name := strings.TrimPrefix(r.URL.Path, "/downloads/")
	if !updateAssetPattern.MatchString(name) {
		http.NotFound(w, r)
		return
	}
	w.Header().Set("Content-Type", "application/octet-stream")
	w.Header().Set("Content-Disposition", `attachment; filename="`+name+`"`)
	w.Header().Set("Cache-Control", "public, max-age=31536000, immutable")
	http.ServeFile(w, r, filepath.Join(s.options.UpdateRoot, name))
}

func securityHeaders(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("Referrer-Policy", "no-referrer")
		w.Header().Set("Permissions-Policy", "camera=(), microphone=(), geolocation=()")
		w.Header().Set("Content-Security-Policy", "default-src 'self'; connect-src 'self' ws: wss:; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'")
		next.ServeHTTP(w, r)
	})
}

func spaHandler(root string) http.Handler {
	fileServer := http.FileServer(http.Dir(root))
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		clean := filepath.Clean(strings.TrimPrefix(r.URL.Path, "/"))
		if clean == "." {
			clean = "index.html"
		}
		isShell := clean == "index.html"
		path := filepath.Join(root, clean)
		if _, err := os.Stat(path); errors.Is(err, fs.ErrNotExist) {
			r.URL.Path = "/"
			path = filepath.Join(root, "index.html")
			isShell = true
		}
		if ext := filepath.Ext(path); ext != "" {
			if contentType := mime.TypeByExtension(ext); contentType != "" {
				w.Header().Set("Content-Type", contentType)
			}
		}
		if isShell || clean == "sw.js" || filepath.Ext(clean) == ".webmanifest" || strings.HasPrefix(filepath.ToSlash(clean), "updates/") {
			w.Header().Set("Cache-Control", "no-cache, no-store, must-revalidate")
		} else if strings.HasPrefix(filepath.ToSlash(clean), "assets/") || strings.HasPrefix(filepath.ToSlash(clean), "downloads/") {
			w.Header().Set("Cache-Control", "public, max-age=31536000, immutable")
		}
		fileServer.ServeHTTP(w, r)
	})
}

func (s *Server) handleBetterGiRelease(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		w.Header().Set("Allow", http.MethodGet)
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}

	s.betterGiMu.Lock()
	defer s.betterGiMu.Unlock()
	if len(s.betterGiCached) > 0 && s.options.Now().Before(s.betterGiCachedUntil) {
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("Cache-Control", "public, max-age=900")
		_, _ = w.Write(s.betterGiCached)
		return
	}

	request, err := http.NewRequestWithContext(r.Context(), http.MethodGet, s.betterGiReleaseURL, nil)
	if err != nil {
		http.Error(w, "release lookup unavailable", http.StatusBadGateway)
		return
	}
	request.Header.Set("Accept", "application/vnd.github+json")
	request.Header.Set("User-Agent", "BetterGI-Remote-Relay")
	response, err := s.releaseClient.Do(request)
	if err != nil {
		http.Error(w, "release lookup unavailable", http.StatusBadGateway)
		return
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK {
		http.Error(w, "release lookup unavailable", http.StatusBadGateway)
		return
	}
	var upstream struct {
		TagName string `json:"tag_name"`
		HTMLURL string `json:"html_url"`
	}
	decoder := json.NewDecoder(io.LimitReader(response.Body, 1<<20))
	if err := decoder.Decode(&upstream); err != nil || !releaseTagPattern.MatchString(upstream.TagName) || !strings.HasPrefix(upstream.HTMLURL, "https://github.com/babalae/better-genshin-impact/") {
		http.Error(w, "invalid release response", http.StatusBadGateway)
		return
	}
	payload, err := json.Marshal(upstream)
	if err != nil {
		http.Error(w, "release lookup unavailable", http.StatusBadGateway)
		return
	}
	s.betterGiCached = payload
	s.betterGiCachedUntil = s.options.Now().Add(15 * time.Minute)
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "public, max-age=900")
	_, _ = w.Write(payload)
}

func (s *Server) handleHealth(w http.ResponseWriter, _ *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(map[string]any{
		"status":      "ok",
		"protocol":    protocolVersion,
		"connections": s.connections.Load(),
	})
}

func (s *Server) handleWebSocket(w http.ResponseWriter, r *http.Request) {
	if s.connections.Load() >= int64(s.options.MaxConnections) {
		http.Error(w, "connection limit reached", http.StatusServiceUnavailable)
		return
	}

	conn, err := s.upgrader.Upgrade(w, r, nil)
	if err != nil {
		return
	}
	s.connections.Add(1)
	defer s.connections.Add(-1)
	defer conn.Close()

	conn.SetReadLimit(maximumRegisterBytes)
	_ = conn.SetReadDeadline(s.options.Now().Add(registrationTimeout))
	messageType, payload, err := conn.ReadMessage()
	if err != nil || messageType != websocket.TextMessage {
		writeClose(conn, websocket.ClosePolicyViolation, "registration required")
		return
	}

	var registration registration
	decoder := json.NewDecoder(strings.NewReader(string(payload)))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&registration); err != nil || !validRegistration(registration) {
		writeClose(conn, websocket.ClosePolicyViolation, "invalid registration")
		return
	}

	joined, err := s.join(conn, registration)
	if err != nil {
		_ = writeControl(conn, controlMessage{Type: "error", Code: err.Error()})
		writeClose(conn, websocket.ClosePolicyViolation, err.Error())
		return
	}
	defer s.leave(joined)

	conn.SetReadLimit(maximumFrameBytes)
	_ = conn.SetReadDeadline(s.options.Now().Add(readPongTimeout))
	conn.SetPongHandler(func(string) error {
		return conn.SetReadDeadline(s.options.Now().Add(readPongTimeout))
	})
	joined.limit = tokenBucket{tokens: rateBurst, lastFill: s.options.Now()}
	_ = joined.sendControl(controlMessage{Type: "registered"})
	s.notifyPeerState(joined.room)

	stopHeartbeat := make(chan struct{})
	go joined.heartbeat(stopHeartbeat)
	defer close(stopHeartbeat)

	for {
		messageType, payload, err = conn.ReadMessage()
		if err != nil {
			return
		}
		if messageType != websocket.BinaryMessage {
			writeClose(conn, websocket.CloseUnsupportedData, "binary application frames only")
			return
		}
		if !joined.limit.allow(s.options.Now()) {
			writeClose(conn, websocket.ClosePolicyViolation, "rate_limited")
			return
		}
		peer := s.peer(joined)
		if peer == nil {
			_ = joined.sendControl(controlMessage{Type: "error", Code: "peer_offline"})
			continue
		}
		if err := peer.sendBinary(payload); err != nil {
			return
		}
	}
}

func (s *Server) join(conn *websocket.Conn, registration registration) (*client, error) {
	s.roomsMu.Lock()

	tokenHash := sha256.Sum256([]byte(registration.RelayToken))
	current := s.rooms[registration.ChannelID]
	if current == nil {
		current = &room{channelID: registration.ChannelID, tokenHash: tokenHash}
		s.rooms[registration.ChannelID] = current
	} else if subtle.ConstantTimeCompare(current.tokenHash[:], tokenHash[:]) != 1 {
		s.roomsMu.Unlock()
		return nil, errors.New("invalid_relay_token")
	}

	joined := &client{conn: conn, server: s, room: current, role: registration.Role}
	var replaced *client
	if registration.Role == "pc" {
		replaced = current.pc
		current.pc = joined
	} else {
		replaced = current.phone
		current.phone = joined
	}
	current.revision++
	s.roomsMu.Unlock()

	if replaced != nil {
		replaced.closeAsReplaced()
	}
	return joined, nil
}

func (s *Server) leave(leaving *client) {
	s.roomsMu.Lock()
	current := s.rooms[leaving.room.channelID]
	if current == nil {
		s.roomsMu.Unlock()
		return
	}
	if leaving.role == "pc" && current.pc == leaving {
		current.pc = nil
	}
	if leaving.role == "phone" && current.phone == leaving {
		current.phone = nil
	}
	current.revision++
	empty := current.pc == nil && current.phone == nil
	if empty {
		delete(s.rooms, current.channelID)
	}
	s.roomsMu.Unlock()
	if !empty {
		s.notifyPeerState(current)
	}
}

func (s *Server) peer(source *client) *client {
	s.roomsMu.Lock()
	defer s.roomsMu.Unlock()
	current := s.rooms[source.room.channelID]
	if current == nil {
		return nil
	}
	if source.role == "pc" {
		if current.pc != source {
			return nil
		}
		return current.phone
	}
	if current.phone != source {
		return nil
	}
	return current.pc
}

func (s *Server) notifyPeerState(current *room) {
	s.roomsMu.Lock()
	pc := current.pc
	phone := current.phone
	revision := current.revision
	s.roomsMu.Unlock()

	if pc != nil {
		online := phone != nil
		_ = pc.sendControl(controlMessage{Type: "peer_status", Online: &online, Revision: revision})
	}
	if phone != nil {
		online := pc != nil
		_ = phone.sendControl(controlMessage{Type: "peer_status", Online: &online, Revision: revision})
	}
}

func (c *client) heartbeat(stop <-chan struct{}) {
	ticker := time.NewTicker(heartbeatInterval)
	defer ticker.Stop()
	for {
		select {
		case <-stop:
			return
		case <-ticker.C:
			c.write.Lock()
			_ = c.conn.SetWriteDeadline(c.server.options.Now().Add(writeTimeout))
			err := c.conn.WriteMessage(websocket.PingMessage, nil)
			c.write.Unlock()
			if err != nil {
				_ = c.conn.Close()
				return
			}
		}
	}
}

func (c *client) sendBinary(payload []byte) error {
	c.write.Lock()
	defer c.write.Unlock()
	_ = c.conn.SetWriteDeadline(c.server.options.Now().Add(writeTimeout))
	return c.conn.WriteMessage(websocket.BinaryMessage, payload)
}

func (c *client) sendControl(message controlMessage) error {
	c.write.Lock()
	defer c.write.Unlock()
	_ = c.conn.SetWriteDeadline(c.server.options.Now().Add(writeTimeout))
	return c.conn.WriteJSON(message)
}

func (c *client) closeAsReplaced() {
	c.write.Lock()
	defer c.write.Unlock()
	_ = c.conn.SetWriteDeadline(c.server.options.Now().Add(writeTimeout))
	_ = c.conn.WriteJSON(controlMessage{Type: "error", Code: "role_replaced"})
	_ = c.conn.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(websocket.CloseNormalClosure, "role replaced"), c.server.options.Now().Add(writeTimeout))
	_ = c.conn.Close()
}

func (b *tokenBucket) allow(now time.Time) bool {
	b.mu.Lock()
	defer b.mu.Unlock()
	elapsed := now.Sub(b.lastFill).Seconds()
	if elapsed > 0 {
		b.tokens += elapsed * ratePerSecond
		if b.tokens > rateBurst {
			b.tokens = rateBurst
		}
		b.lastFill = now
	}
	if b.tokens < 1 {
		return false
	}
	b.tokens--
	return true
}

func (s *Server) checkOrigin(r *http.Request) bool {
	origin := r.Header.Get("Origin")
	if origin == "" {
		return true
	}
	if s.options.AllowedOrigin != "" {
		return strings.EqualFold(strings.TrimRight(origin, "/"), strings.TrimRight(s.options.AllowedOrigin, "/"))
	}
	return strings.EqualFold(strings.TrimPrefix(strings.TrimPrefix(origin, "https://"), "http://"), r.Host)
}

func validRegistration(value registration) bool {
	return value.Type == "register" &&
		value.ProtocolVersion == protocolVersion &&
		channelPattern.MatchString(value.ChannelID) &&
		tokenPattern.MatchString(value.RelayToken) &&
		(value.Role == "pc" || value.Role == "phone")
}

func writeControl(conn *websocket.Conn, message controlMessage) error {
	_ = conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	return conn.WriteJSON(message)
}

func writeClose(conn *websocket.Conn, code int, reason string) {
	_ = conn.SetWriteDeadline(time.Now().Add(writeTimeout))
	_ = conn.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(code, reason), time.Now().Add(writeTimeout))
}
