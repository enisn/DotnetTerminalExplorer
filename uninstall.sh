#!/usr/bin/env bash
set -e

# Dotnet Terminal Explorer (dte) - Standalone Binary Uninstaller
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/uninstall.sh | bash
#
# Environment variables:
#   INSTALL_DIR - Custom directory where dte was installed (default: ~/.local/bin or /usr/local/bin)

INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"

echo "==> Uninstalling Dotnet Terminal Explorer (dte)..."

REMOVED=0

# Check common install locations
for dir in "$INSTALL_DIR" "/usr/local/bin" "$HOME/.local/bin"; do
    if [ -f "$dir/dte" ]; then
        echo "    Removing $dir/dte"
        rm -f "$dir/dte"
        REMOVED=1
    fi
    if [ -f "$dir/DotnetTerminalExplorer" ]; then
        echo "    Removing $dir/DotnetTerminalExplorer"
        rm -f "$dir/DotnetTerminalExplorer"
        REMOVED=1
    fi
    if [ -f "$dir/libonigwrap.so" ]; then
        echo "    Removing $dir/libonigwrap.so"
        rm -f "$dir/libonigwrap.so"
    fi
    if [ -f "$dir/libonigwrap.dylib" ]; then
        echo "    Removing $dir/libonigwrap.dylib"
        rm -f "$dir/libonigwrap.dylib"
    fi
done

echo ""
if [ "$REMOVED" -eq 1 ]; then
    echo "================================================================="
    echo " 🗑️  Dotnet Terminal Explorer (dte) successfully uninstalled."
    echo "================================================================="
else
    echo "================================================================="
    echo " ℹ️  No installation of 'dte' found in standard locations."
    echo "================================================================="
fi
echo ""
