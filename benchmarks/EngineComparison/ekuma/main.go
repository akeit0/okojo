package main

import (
	"context"
	"fmt"
	"log"
	"os"

	"github.com/hajimehoshi/ekuma"
)

func main() {
	if len(os.Args) != 2 {
		log.Fatal("usage: ekuma-runner <script>")
	}
	path := os.Args[1]
	source, err := os.ReadFile(path)
	if err != nil {
		log.Fatal(err)
	}
	var vm ekuma.VM
	script, err := vm.ParseScript(path, string(source))
	if err != nil {
		log.Fatal(err)
	}
	result, err := script.Evaluate(context.Background())
	if err != nil {
		log.Fatal(err)
	}
	if result.Type() != ekuma.TypeString {
		log.Fatalf("expected a string result, got %s", result.Type())
	}
	fmt.Println(result.String())
}
