package main

import (
	"fmt"
	"net/http"
	"os"
	"runtime"
)

func main() {
	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}

	greeting := os.Getenv("GREETING")
	if greeting == "" {
		greeting = "GREETING was not set"
	}

	http.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		fmt.Fprintf(w, "go: %s (%s)", greeting, runtime.Version())
	})

	fmt.Printf("goapp listening on port %s with %s\n", port, runtime.Version())
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
