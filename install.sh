#!/usr/bin/env bash
set -e

# Dotnet Terminal Explorer (dte) - Standalone Binary Installer
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/install.sh | bash
#
# Environment variables:
#   DTE_VERSION  - Specific release tag to install (default: latest)
#   INSTALL_DIR  - Target install directory (default: ~/.local/bin or /usr/local/bin if root)
#   GITHUB_TOKEN - Optional GitHub token for private repository access

REPO="enisn/DotnetTerminalExplorer"

print_error_and_fallback() {
    local reason="$1"
    echo ""
    echo "================================================================="
    echo " ⚠️  $reason"
    echo "================================================================="
    echo ""
    echo " Standalone native binaries are not available for your platform,"
    echo " but you can still run Dotnet Terminal Explorer using the .NET tool:"
    echo ""
    echo "   dotnet tool install --global DotnetTerminalExplorer"
    echo ""
    echo " For more information, visit: https://github.com/$REPO"
    echo "================================================================="
    echo ""
    exit 1
}

# 1. Detect Operating System
OS_TYPE="$(uname -s)"
case "$OS_TYPE" in
    Linux*)  OS="linux" ;;
    Darwin*) OS="osx" ;;
    *)       print_error_and_fallback "Unsupported Operating System: '$OS_TYPE'" ;;
esac

# 2. Detect CPU Architecture
ARCH_TYPE="$(uname -m)"
case "$ARCH_TYPE" in
    x86_64|amd64)   ARCH="x64" ;;
    aarch64|arm64)  ARCH="arm64" ;;
    *)              print_error_and_fallback "Unsupported CPU Architecture: '$ARCH_TYPE'" ;;
esac

RID="${OS}-${ARCH}"

if [ "$OS" = "osx" ] && [ "$ARCH" = "x64" ]; then
    print_error_and_fallback "macOS on Intel (x86_64) is not available as a standalone binary."
fi

# 3. Check glibc version on Linux
if [ "$OS" = "linux" ]; then
    GLIBC_VER=""
    if command -v getconf >/dev/null 2>&1; then
        GLIBC_VER="$(getconf GNU_LIBC_VERSION 2>/dev/null | awk '{print $NF}')"
    fi
    if [ -z "$GLIBC_VER" ] && command -v ldd >/dev/null 2>&1; then
        GLIBC_VER="$(ldd --version 2>&1 | head -n 1 | grep -oE '[0-9]+\.[0-9]+' | head -n 1)"
    fi

    if [ -n "$GLIBC_VER" ]; then
        GLIBC_MAJOR="$(echo "$GLIBC_VER" | cut -d. -f1)"
        GLIBC_MINOR="$(echo "$GLIBC_VER" | cut -d. -f2)"

        # Minimum required glibc version (>= 2.28)
        if [ "$GLIBC_MAJOR" -lt 2 ] || { [ "$GLIBC_MAJOR" -eq 2 ] && [ "$GLIBC_MINOR" -lt 28 ]; }; then
            print_error_and_fallback "Your system glibc ($GLIBC_VER) is older than required (>= 2.28)."
        fi
    fi
fi

# 3. Determine Installation Directory
if [ -z "$INSTALL_DIR" ]; then
    if [ "$(id -u)" -eq 0 ]; then
        INSTALL_DIR="/usr/local/bin"
    else
        INSTALL_DIR="$HOME/.local/bin"
    fi
fi

mkdir -p "$INSTALL_DIR"

echo "==> Installing Dotnet Terminal Explorer (dte)..."
echo "    Platform detected: $RID"
echo "    Target directory:  $INSTALL_DIR"

# 4. Resolve Version
VERSION="${DTE_VERSION:-latest}"

# Construct download URL
if [ -n "$LOCAL_ARCHIVE" ] && [ -f "$LOCAL_ARCHIVE" ]; then
    echo "    Using local archive: $LOCAL_ARCHIVE"
    ARCHIVE_PATH="$LOCAL_ARCHIVE"
else
    TEMP_DIR="$(mktemp -d)"
    trap 'rm -rf "$TEMP_DIR"' EXIT

    ARCHIVE_PATH="$TEMP_DIR/dte.tar.gz"

    if [ "$VERSION" = "latest" ]; then
        DOWNLOAD_URL="https://github.com/$REPO/releases/latest/download/dte-latest-${RID}.tar.gz"
        API_URL="https://api.github.com/repos/$REPO/releases/latest"
    else
        DOWNLOAD_URL="https://github.com/$REPO/releases/download/${VERSION}/dte-${VERSION}-${RID}.tar.gz"
        API_URL="https://api.github.com/repos/$REPO/releases/tags/${VERSION}"
    fi

    # Try resolving exact filename from GitHub API if curl/jq is available or fallback to direct URL
    AUTH_HEADER=()
    if [ -n "$GITHUB_TOKEN" ]; then
        AUTH_HEADER=(-H "Authorization: Bearer $GITHUB_TOKEN")
    fi

    # Attempt download via GitHub Release URL
    echo "    Downloading release asset for $RID..."
    
    # Try fetching release asset directly
    DOWNLOAD_SUCCESS=0

    # If version is latest, resolve actual tag name first from GitHub API
    if [ "$VERSION" = "latest" ]; then
        RELEASE_JSON=$(curl -sSL "${AUTH_HEADER[@]}" "$API_URL" 2>/dev/null || true)
        RESOLVED_TAG=$(echo "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | head -n 1 | cut -d '"' -f 4 || true)
        
        if [ -n "$RESOLVED_TAG" ]; then
            VERSION="$RESOLVED_TAG"
            DOWNLOAD_URL="https://github.com/$REPO/releases/download/${VERSION}/dte-${VERSION}-${RID}.tar.gz"
        fi
    fi

    if curl -fSL "${AUTH_HEADER[@]}" "$DOWNLOAD_URL" -o "$ARCHIVE_PATH" 2>/dev/null; then
        DOWNLOAD_SUCCESS=1
    elif [ "$VERSION" = "latest" ]; then
        # Try generic latest filename fallback
        FALLBACK_URL="https://github.com/$REPO/releases/latest/download/dte-linux-${ARCH}.tar.gz"
        if curl -fSL "${AUTH_HEADER[@]}" "$FALLBACK_URL" -o "$ARCHIVE_PATH" 2>/dev/null; then
            DOWNLOAD_SUCCESS=1
        fi
    fi

    if [ "$DOWNLOAD_SUCCESS" -ne 1 ]; then
        echo ""
        echo "❌ Failed to download release asset from: $DOWNLOAD_URL"
        print_error_and_fallback "Could not find a prebuilt release for $RID ($VERSION)."
    fi
fi

# 5. Extract and Install
echo "    Extracting binary..."
EXTRACT_DIR="$(mktemp -d)"
trap 'rm -rf "$EXTRACT_DIR" "$TEMP_DIR"' EXIT

tar -xzf "$ARCHIVE_PATH" -C "$EXTRACT_DIR"

# Copy binary
if [ -f "$EXTRACT_DIR/dte" ]; then
    cp -f "$EXTRACT_DIR/dte" "$INSTALL_DIR/dte"
elif [ -f "$EXTRACT_DIR/DotnetTerminalExplorer" ]; then
    cp -f "$EXTRACT_DIR/DotnetTerminalExplorer" "$INSTALL_DIR/dte"
else
    echo "❌ Executable binary not found in archive."
    exit 1
fi

chmod +x "$INSTALL_DIR/dte"

# Copy companion native libraries (e.g. libonigwrap)
find "$EXTRACT_DIR" -maxdepth 1 -name "*.so" -o -name "*.dylib" | while read -r lib; do
    if [ -f "$lib" ]; then
        cp -f "$lib" "$INSTALL_DIR/"
    fi
done

echo ""
echo "================================================================="
echo " 🎉 Dotnet Terminal Explorer successfully installed to:"
echo "    $INSTALL_DIR/dte"
echo "================================================================="

# 6. Check PATH
case ":$PATH:" in
    *":$INSTALL_DIR:"*) ;;
    *)
        echo ""
        echo " ⚠️  Note: '$INSTALL_DIR' is not in your current PATH."
        echo "    Add it to your PATH by adding the following line to your ~/.bashrc or ~/.zshrc:"
        echo ""
        echo "    export PATH=\"$INSTALL_DIR:\$PATH\""
        echo ""
        ;;
esac

echo " Run 'dte' to start exploring your directories!"
echo ""
