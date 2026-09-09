package relay

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

const (
	testChannel = "MDEyMzQ1Njc4OWFiY2RlZg"
	testToken   = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY"
)

func TestRelayForwardsBinaryFrames(t *testing.T) {
	server := httptest.NewServer(NewServer(Options{MaxConnections: 10}).Handler(""))
	defer server.Close()
	url := "ws" + strings.TrimPrefix(server.URL, "http") + "/ws"

	pc := connectAndRegister(t, url, "pc", testToken)
	defer pc.Close()
	phone := connectAndRegister(t, url, "phone", testToken)
	defer phone.Close()

	waitForPeerOnline(t, pc)
	waitForPeerOnline(t, phone)

	payload := []byte(`{"encrypted":"payload"}`)
	if err := phone.WriteMessage(websocket.BinaryMessage, payload); err != nil {
		t.Fatal(err)
	}
	var messageType int
	var received []byte
	var err error
	for messageType != websocket.BinaryMessage {
		messageType, received, err = pc.ReadMessage()
		if err != nil {
			t.Fatal(err)
		}
	}
	if messageType != websocket.BinaryMessage || string(received) != string(payload) {
		t.Fatalf("unexpected forwarded frame: type=%d body=%s", messageType, received)
	}
}

func TestRelayRejectsMismatchedToken(t *testing.T) {
	server := httptest.NewServer(NewServer(Options{MaxConnections: 10}).Handler(""))
	defer server.Close()
	url := "ws" + strings.TrimPrefix(server.URL, "http") + "/ws"

	pc := connectAndRegister(t, url, "pc", testToken)
	defer pc.Close()

	phone := dial(t, url)
	defer phone.Close()
	register(t, phone, "phone", "YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXowMTIzNDU")
	_, payload, err := phone.ReadMessage()
	if err != nil {
		t.Fatal(err)
	}
	var message controlMessage
	if err := json.Unmarshal(payload, &message); err != nil {
		t.Fatal(err)
	}
	if message.Code != "invalid_relay_token" {
		t.Fatalf("unexpected error code: %s", message.Code)
	}
}

func TestRelayNewConnectionReplacesSameRole(t *testing.T) {
	server := httptest.NewServer(NewServer(Options{MaxConnections: 10}).Handler(""))
	defer server.Close()
	url := "ws" + strings.TrimPrefix(server.URL, "http") + "/ws"

	firstPhone := connectAndRegister(t, url, "phone", testToken)
	defer firstPhone.Close()
	pc := connectAndRegister(t, url, "pc", testToken)
	defer pc.Close()
	waitForPeerOnline(t, firstPhone)
	waitForPeerOnline(t, pc)

	secondPhone := connectAndRegister(t, url, "phone", testToken)
	defer secondPhone.Close()
	waitForPeerOnline(t, secondPhone)

	for {
		_, payload, err := firstPhone.ReadMessage()
		if err != nil {
			break
		}
		var message controlMessage
		if json.Unmarshal(payload, &message) == nil && message.Code == "role_replaced" {
			break
		}
	}

	payload := []byte(`{"encrypted":"new-phone"}`)
	if err := secondPhone.WriteMessage(websocket.BinaryMessage, payload); err != nil {
		t.Fatal(err)
	}
	var messageType int
	var received []byte
	var err error
	for messageType != websocket.BinaryMessage {
		messageType, received, err = pc.ReadMessage()
		if err != nil {
			t.Fatal(err)
		}
	}
	if messageType != websocket.BinaryMessage || string(received) != string(payload) {
		t.Fatalf("replacement did not receive forwarding: type=%d body=%s", messageType, received)
	}
}

func TestHealthEndpoint(t *testing.T) {
	server := httptest.NewServer(NewServer(Options{MaxConnections: 10}).Handler(""))
	defer server.Close()
	response, err := server.Client().Get(server.URL + "/healthz")
	if err != nil {
		t.Fatal(err)
	}
	defer response.Body.Close()
	if response.StatusCode != 200 {
		t.Fatalf("unexpected status: %d", response.StatusCode)
	}
}

func TestSpaCacheHeadersKeepShellFresh(t *testing.T) {
	root := t.TempDir()
	if err := os.Mkdir(filepath.Join(root, "assets"), 0o755); err != nil {
		t.Fatal(err)
	}
	for path, body := range map[string]string{
		"index.html":           "<html></html>",
		"sw.js":                "self.skipWaiting()",
		"manifest.webmanifest": `{}`,
		"assets/index-abc.js":  "export {}",
	} {
		if err := os.WriteFile(filepath.Join(root, filepath.FromSlash(path)), []byte(body), 0o644); err != nil {
			t.Fatal(err)
		}
	}

	server := httptest.NewServer(NewServer(Options{MaxConnections: 10}).Handler(root))
	defer server.Close()

	for path, expected := range map[string]string{
		"/":                     "no-cache, no-store, must-revalidate",
		"/unknown-route":        "no-cache, no-store, must-revalidate",
		"/sw.js":                "no-cache, no-store, must-revalidate",
		"/manifest.webmanifest": "no-cache, no-store, must-revalidate",
		"/assets/index-abc.js":  "public, max-age=31536000, immutable",
	} {
		response, err := http.Get(server.URL + path)
		if err != nil {
			t.Fatal(err)
		}
		_ = response.Body.Close()
		if actual := response.Header.Get("Cache-Control"); actual != expected {
			t.Fatalf("%s cache header = %q, want %q", path, actual, expected)
		}
	}
}

func connectAndRegister(t *testing.T, url, role, token string) *websocket.Conn {
	t.Helper()
	conn := dial(t, url)
	register(t, conn, role, token)
	for {
		_, payload, err := conn.ReadMessage()
		if err != nil {
			t.Fatal(err)
		}
		var message controlMessage
		if json.Unmarshal(payload, &message) == nil && message.Type == "registered" {
			return conn
		}
	}
}

func dial(t *testing.T, url string) *websocket.Conn {
	t.Helper()
	conn, _, err := websocket.DefaultDialer.Dial(url, nil)
	if err != nil {
		t.Fatal(err)
	}
	_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	return conn
}

func register(t *testing.T, conn *websocket.Conn, role, token string) {
	t.Helper()
	if err := conn.WriteJSON(registration{
		Type:            "register",
		ProtocolVersion: protocolVersion,
		ChannelID:       testChannel,
		Role:            role,
		RelayToken:      token,
	}); err != nil {
		t.Fatal(err)
	}
}

func waitForPeerOnline(t *testing.T, conn *websocket.Conn) {
	t.Helper()
	for {
		_, payload, err := conn.ReadMessage()
		if err != nil {
			t.Fatal(err)
		}
		var message controlMessage
		if json.Unmarshal(payload, &message) == nil && message.Type == "peer_status" && message.Online != nil && *message.Online {
			return
		}
	}
}
