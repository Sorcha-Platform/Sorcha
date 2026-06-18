#!/usr/bin/env bash
# configure-android-signing.sh — wire release signing + CI version override into the
# generated Capacitor Android project, idempotently, WITHOUT putting any secret in gradle.
#
# Appends a managed `android { }` block (Gradle merges blocks) that:
#   - loads the Mac-only ~/.sorcha-signing/keystore.properties at build time and applies it
#     to the release build type (no secret in the gradle file), and
#   - overrides versionName/versionCode from -PsorchaVersionName / -PsorchaVersionCode when
#     CI passes them (falls back to the template values otherwise).
#
# Replace-on-run: the managed block (between markers) is rewritten each run, so edits here
# propagate. Safe to re-run.
#
# Env: SORCHA_REPO (default ~/projects/Sorcha)

set -euo pipefail
REPO="${SORCHA_REPO:-$HOME/projects/Sorcha}"
GRADLE="$REPO/mobile/wallet/android/app/build.gradle"
BEGIN="// >>> sorcha config >>>"
END="// <<< sorcha config <<<"

[ -f "$GRADLE" ] || { echo "XX $GRADLE not found (run 'npx cap add android' first)"; exit 1; }

# strip any existing managed block so edits propagate (no duplicates)
if grep -qF "$BEGIN" "$GRADLE"; then
  sed -i '' "/$BEGIN/,/$END/d" "$GRADLE"
fi
# strip the legacy marker name from earlier versions of this script
if grep -qF "// >>> sorcha signing >>>" "$GRADLE"; then
  sed -i '' "/\/\/ >>> sorcha signing >>>/,/\/\/ <<< sorcha signing <<</d" "$GRADLE"
fi

cat >> "$GRADLE" <<'GRADLE_EOF'

// >>> sorcha config >>>
// Release signing + CI version override. No secret lives here — keystore.properties is
// read from the Mac-only ~/.sorcha-signing at build time.
android {
    signingConfigs {
        release {
            def props = new Properties()
            def f = file(System.getProperty("user.home") + "/.sorcha-signing/keystore.properties")
            if (f.exists()) {
                f.withInputStream { props.load(it) }
                storeFile file(props.getProperty('storeFile'))
                storePassword props.getProperty('storePassword')
                keyAlias props.getProperty('keyAlias')
                keyPassword props.getProperty('keyPassword')
            } else {
                println "WARNING: ~/.sorcha-signing/keystore.properties missing — release build will be unsigned"
            }
        }
    }
    buildTypes {
        release {
            signingConfig signingConfigs.release
        }
    }
    defaultConfig {
        if (project.hasProperty('sorchaVersionName')) { versionName project.property('sorchaVersionName') }
        if (project.hasProperty('sorchaVersionCode')) { versionCode project.property('sorchaVersionCode').toInteger() }
    }
}
// <<< sorcha config <<<
GRADLE_EOF

echo "OK wrote sorcha config block (signing + version override) to $GRADLE"
