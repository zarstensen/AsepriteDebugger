set BUILDKIT_PROGRESS=auto
echo Docker Build && docker compose build debugger && echo Docker Prune && docker image prune -f && echo Docker Run && docker compose run --rm debugger