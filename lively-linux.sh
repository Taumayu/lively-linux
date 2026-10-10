#!/usr/bin/env bash
export PATH="$HOME/.dotnet:$PATH"

DAEMON_BIN="$HOME/.local/share/lively-linux/daemon/Lively.Linux.Daemon"
UI_BIN="$HOME/.local/share/lively-linux/ui/Lively.Linux.UI"
SOCKET_PATH="/tmp/lively-grpc.sock"

# Start the daemon if not already running
if ! pgrep -f "$DAEMON_BIN" > /dev/null; then
    "$DAEMON_BIN" > /dev/null 2>&1 &
    for i in {1..30}; do
        if [ -S "$SOCKET_PATH" ]; then
            break
        fi
        sleep 0.1
    done
fi

# Run the UI
exec "$UI_BIN" "$@"
