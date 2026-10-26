# bash documentation sample

Forty fenced blocks, as a documentation site renders them. Generated from short hand-written snippets.

## Example 1

````bash
#!/usr/bin/env bash
set -euo pipefail

for file in *.log; do
  echo "Processing $file"
  gzip "$file"
done
````

## Example 2

````bash
dotnet new console -n Hello
cd Hello
dotnet run
````

## Example 3

````bash
if [[ -z "${API_KEY:-}" ]]; then
  echo "API_KEY is not set" >&2
  exit 1
fi
````

## Example 4

````bash
git checkout -b feature/docs
git add -A && git commit -m "Update docs"
git push -u origin feature/docs
````

## Example 5

````bash
curl -fsSL https://example.com/install.sh | sh -s -- --version 1.2.3
````

## Example 6

````bash
function retry() {
  local attempts=$1; shift
  until "$@"; do
    (( attempts-- )) || return 1
    sleep 2
  done
}
````

## Example 7

````bash
export PATH="$HOME/.dotnet/tools:$PATH"
alias ll='ls -alF'

````

## Example 8

````bash
cat <<EOF > config.ini
[server]
port = 8080
EOF
````

## Example 9

````bash
find . -name "*.cs" -type f -print0 | xargs -0 grep -l "TODO" | sort | uniq -c
````

## Example 10

````bash
case "$1" in
  start) systemctl start app ;;
  stop) systemctl stop app ;;
  *) echo "usage: $0 {start|stop}"; exit 2 ;;
esac
````

## Example 11

````bash
#!/usr/bin/env bash
set -euo pipefail

for file in *.log; do
  echo "Processing $file"
  gzip "$file"
done
````

## Example 12

````bash
dotnet new console -n Hello
cd Hello
dotnet run
````

## Example 13

````bash
if [[ -z "${API_KEY:-}" ]]; then
  echo "API_KEY is not set" >&2
  exit 1
fi
````

## Example 14

````bash
git checkout -b feature/docs
git add -A && git commit -m "Update docs"
git push -u origin feature/docs
````

## Example 15

````bash
curl -fsSL https://example.com/install.sh | sh -s -- --version 1.2.3
````

## Example 16

````bash
function retry() {
  local attempts=$1; shift
  until "$@"; do
    (( attempts-- )) || return 1
    sleep 2
  done
}
````

## Example 17

````bash
export PATH="$HOME/.dotnet/tools:$PATH"
alias ll='ls -alF'

````

## Example 18

````bash
cat <<EOF > config.ini
[server]
port = 8080
EOF
````

## Example 19

````bash
find . -name "*.cs" -type f -print0 | xargs -0 grep -l "TODO" | sort | uniq -c
````

## Example 20

````bash
case "$1" in
  start) systemctl start app ;;
  stop) systemctl stop app ;;
  *) echo "usage: $0 {start|stop}"; exit 2 ;;
esac
````

## Example 21

````bash
#!/usr/bin/env bash
set -euo pipefail

for file in *.log; do
  echo "Processing $file"
  gzip "$file"
done
````

## Example 22

````bash
dotnet new console -n Hello
cd Hello
dotnet run
````

## Example 23

````bash
if [[ -z "${API_KEY:-}" ]]; then
  echo "API_KEY is not set" >&2
  exit 1
fi
````

## Example 24

````bash
git checkout -b feature/docs
git add -A && git commit -m "Update docs"
git push -u origin feature/docs
````

## Example 25

````bash
curl -fsSL https://example.com/install.sh | sh -s -- --version 1.2.3
````

## Example 26

````bash
function retry() {
  local attempts=$1; shift
  until "$@"; do
    (( attempts-- )) || return 1
    sleep 2
  done
}
````

## Example 27

````bash
export PATH="$HOME/.dotnet/tools:$PATH"
alias ll='ls -alF'

````

## Example 28

````bash
cat <<EOF > config.ini
[server]
port = 8080
EOF
````

## Example 29

````bash
find . -name "*.cs" -type f -print0 | xargs -0 grep -l "TODO" | sort | uniq -c
````

## Example 30

````bash
case "$1" in
  start) systemctl start app ;;
  stop) systemctl stop app ;;
  *) echo "usage: $0 {start|stop}"; exit 2 ;;
esac
````

## Example 31

````bash
#!/usr/bin/env bash
set -euo pipefail

for file in *.log; do
  echo "Processing $file"
  gzip "$file"
done
````

## Example 32

````bash
dotnet new console -n Hello
cd Hello
dotnet run
````

## Example 33

````bash
if [[ -z "${API_KEY:-}" ]]; then
  echo "API_KEY is not set" >&2
  exit 1
fi
````

## Example 34

````bash
git checkout -b feature/docs
git add -A && git commit -m "Update docs"
git push -u origin feature/docs
````

## Example 35

````bash
curl -fsSL https://example.com/install.sh | sh -s -- --version 1.2.3
````

## Example 36

````bash
function retry() {
  local attempts=$1; shift
  until "$@"; do
    (( attempts-- )) || return 1
    sleep 2
  done
}
````

## Example 37

````bash
export PATH="$HOME/.dotnet/tools:$PATH"
alias ll='ls -alF'

````

## Example 38

````bash
cat <<EOF > config.ini
[server]
port = 8080
EOF
````

## Example 39

````bash
find . -name "*.cs" -type f -print0 | xargs -0 grep -l "TODO" | sort | uniq -c
````

## Example 40

````bash
case "$1" in
  start) systemctl start app ;;
  stop) systemctl stop app ;;
  *) echo "usage: $0 {start|stop}"; exit 2 ;;
esac
````
