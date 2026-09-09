package main

import (
	"log"
	"net/http"
	"os"
	"strconv"
	"time"

	"github.com/bettergi-remote-lite/relay/internal/relay"
)

func main() {
	port := env("PORT", "8080")
	listenAddress := env("LISTEN_ADDRESS", "127.0.0.1:"+port)
	maxConnections := envInt("MAX_CONNECTIONS", 200)
	allowedOrigin := os.Getenv("ALLOWED_ORIGIN")
	webRoot := env("WEB_ROOT", "")

	server := relay.NewServer(relay.Options{
		AllowedOrigin:  allowedOrigin,
		MaxConnections: maxConnections,
		Now:            time.Now,
	})

	httpServer := &http.Server{
		Addr:              listenAddress,
		Handler:           server.Handler(webRoot),
		ReadHeaderTimeout: 10 * time.Second,
		IdleTimeout:       75 * time.Second,
		WriteTimeout:      15 * time.Second,
		ErrorLog:          log.New(os.Stderr, "http: ", log.LstdFlags),
	}

	log.Printf("BetterGI Remote Lite relay listening on %s", listenAddress)
	if err := httpServer.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		log.Fatal(err)
	}
}

func env(name, fallback string) string {
	if value := os.Getenv(name); value != "" {
		return value
	}
	return fallback
}

func envInt(name string, fallback int) int {
	value, err := strconv.Atoi(os.Getenv(name))
	if err != nil || value <= 0 {
		return fallback
	}
	return value
}
