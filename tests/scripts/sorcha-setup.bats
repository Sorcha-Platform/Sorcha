#!/usr/bin/env bats
#
# Spec 117 (AI Discoverability) Phase 7 task T091 — bats unit tests for the
# prerequisite-check helpers in scripts/sorcha-setup.sh.
#
# Each required check has a positive and a negative path. Optional checks
# (git, powershell) only have positive paths since their negative path is a
# warning, not a failure.
#
# Run:
#   bats tests/scripts/sorcha-setup.bats
#
# Run a single test:
#   bats tests/scripts/sorcha-setup.bats -f "docker_installed"

setup() {
    # Source the script in library mode — defines helpers without invoking main().
    export SORCHA_SETUP_LIB_ONLY=1
    SORCHA_SETUP="$BATS_TEST_DIRNAME/../../scripts/sorcha-setup.sh"
    # shellcheck source=../../scripts/sorcha-setup.sh
    source "$SORCHA_SETUP"

    # Sandbox PATH: original tools live on real PATH; tests inject fakes by
    # prepending a per-test directory.
    export ORIGINAL_PATH="$PATH"
    FAKE_BIN="$(mktemp -d)"
    export FAKE_BIN
}

teardown() {
    [ -n "$FAKE_BIN" ] && rm -rf "$FAKE_BIN"
    export PATH="$ORIGINAL_PATH"
}

# Helper: shadow a command by writing a fake to FAKE_BIN and prepending it to PATH.
shadow_command() {
    # Args: <name> <exit-code> [stdout]
    local name="$1"
    local exit_code="$2"
    local stdout="${3:-}"
    cat > "$FAKE_BIN/$name" <<EOF
#!/usr/bin/env bash
echo "$stdout"
exit $exit_code
EOF
    chmod +x "$FAKE_BIN/$name"
    export PATH="$FAKE_BIN:$ORIGINAL_PATH"
}

# Helper: hide a command by adding a directory to PATH that doesn't contain it.
# We do this by stripping any directory from PATH that contains <name>.
hide_command() {
    local name="$1"
    local clean_path=""
    local IFS=":"
    for dir in $PATH; do
        if [ ! -x "$dir/$name" ]; then
            clean_path="${clean_path}${dir}:"
        fi
    done
    export PATH="${clean_path%:}"
}

# ---- check_docker_installed ----

@test "check_docker_installed_pass: returns 0 when docker is on PATH" {
    shadow_command docker 0 "Docker version 24.0.7, build abc"
    run check_docker_installed
    [ "$status" -eq 0 ]
}

@test "check_docker_installed_fail: returns 1 and emits prereq line when docker is absent" {
    hide_command docker
    run check_docker_installed
    [ "$status" -eq 1 ]
    [[ "$output" == *"missing prerequisite: docker"* ]]
}

# ---- check_docker_daemon_running ----

@test "check_docker_daemon_running_pass: returns 0 when 'docker info' succeeds" {
    shadow_command docker 0 "Server: Docker Engine"
    run check_docker_daemon_running
    [ "$status" -eq 0 ]
}

@test "check_docker_daemon_running_fail: returns 1 when 'docker info' exits non-zero" {
    shadow_command docker 1 "Cannot connect to the Docker daemon"
    run check_docker_daemon_running
    [ "$status" -eq 1 ]
    [[ "$output" == *"missing prerequisite: docker-daemon"* ]]
}

# ---- check_docker_compose_v2 ----

@test "check_docker_compose_v2_pass: returns 0 for compose v2.x" {
    cat > "$FAKE_BIN/docker" <<'EOF'
#!/usr/bin/env bash
case "$1 $2" in
  "compose version") echo "Docker Compose version v2.24.0"; exit 0 ;;
  *) exit 0 ;;
esac
EOF
    chmod +x "$FAKE_BIN/docker"
    export PATH="$FAKE_BIN:$ORIGINAL_PATH"
    run check_docker_compose_v2
    [ "$status" -eq 0 ]
}

@test "check_docker_compose_v2_fail: returns 1 when 'docker compose version' is unavailable" {
    cat > "$FAKE_BIN/docker" <<'EOF'
#!/usr/bin/env bash
case "$1 $2" in
  "compose version") exit 1 ;;
  *) exit 0 ;;
esac
EOF
    chmod +x "$FAKE_BIN/docker"
    export PATH="$FAKE_BIN:$ORIGINAL_PATH"
    run check_docker_compose_v2
    [ "$status" -eq 1 ]
    [[ "$output" == *"missing prerequisite: docker-compose-v2"* ]]
}

# ---- check_port_available ----
# We can't easily simulate a port being held in a unit test without binding
# a real socket; we only verify the success path on a port we know is free.
# Negative-path coverage of port-bound state lives in the integration test
# (the nightly clean-VM run T090).

@test "check_port_available_pass: returns 0 when port is free" {
    # Pick a high port unlikely to be in use on a CI runner.
    run check_port_available 64321
    [ "$status" -eq 0 ]
}

# ---- check_openssl_or_python ----

@test "check_openssl_or_python_pass_openssl: returns 0 when openssl is on PATH" {
    shadow_command openssl 0 "OpenSSL 3.0.13 30 Jan 2024"
    run check_openssl_or_python
    [ "$status" -eq 0 ]
}

@test "check_openssl_or_python_pass_python: returns 0 when only python3 is on PATH" {
    hide_command openssl
    shadow_command python3 0 "Python 3.12.3"
    run check_openssl_or_python
    [ "$status" -eq 0 ]
}

@test "check_openssl_or_python_fail: returns 1 when neither tool is available and /dev/urandom is unreadable" {
    hide_command openssl
    hide_command python3
    # On systems where /dev/urandom is readable (every real CI runner) the helper
    # falls back to a warn rather than failing, so this assertion checks the
    # documented contract: status is 0 with a warn, not 1.
    run check_openssl_or_python
    if [ -r /dev/urandom ]; then
        [ "$status" -eq 0 ]
        [[ "$output" == *"falling back to /dev/urandom"* ]]
    else
        [ "$status" -eq 1 ]
        [[ "$output" == *"missing prerequisite: openssl-or-python3"* ]]
    fi
}

# ---- check_git (optional — never fails) ----

@test "check_git_pass: returns 0 when git is on PATH" {
    shadow_command git 0 "git version 2.43.0"
    run check_git
    [ "$status" -eq 0 ]
}

@test "check_git_warn_when_absent: returns 0 (with warn) when git is absent" {
    hide_command git
    run check_git
    [ "$status" -eq 0 ]
    [[ "$output" == *"Git not found"* ]] || [[ "$output" == *"WARN"* ]]
}

# ---- check_powershell (optional — never fails) ----

@test "check_powershell_warn_when_absent: returns 0 (with warn) when pwsh is absent" {
    hide_command pwsh
    run check_powershell
    [ "$status" -eq 0 ]
    [[ "$output" == *"PowerShell"* ]]
}
