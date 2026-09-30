#!/bin/sh

PORT="$(awk '{print $1}' /var/lib/cehoproxy/panel.port 2>/dev/null)"
exec xdg-open "http://127.0.0.1:${PORT:-8899}"
