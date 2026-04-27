#!/bin/bash
# ════════════════════════════════════════════════════════════════════
# Deploy-Skript für Schaub HR-System
# Mac → Server (Infomaniak VPS)
#
# Usage:  ./deploy.sh
# ════════════════════════════════════════════════════════════════════

set -e  # Bei jedem Fehler abbrechen

PROJECT_DIR="/Users/Walter/projects/hr-system"
SERVER_USER="ubuntu"
SERVER_IP="83.228.209.119"
TARBALL="$HOME/hr-system-publish.tar.gz"

cd "$PROJECT_DIR"

echo "── 1/4 dotnet publish ──"
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

echo "── 2/4 Tar packen ──"
tar -czf "$TARBALL" -C ./publish .
SIZE=$(du -h "$TARBALL" | cut -f1)
echo "    $TARBALL ($SIZE)"

echo "── 3/4 Hochladen ──"
scp "$TARBALL" "$SERVER_USER@$SERVER_IP:~/"

echo "── 4/4 Server-Deploy ──"
ssh "$SERVER_USER@$SERVER_IP" 'bash -s' <<'REMOTE'
set -e
sudo systemctl stop hr-system
sudo rm -rf /var/www/hr-system/*
sudo tar -xzf ~/hr-system-publish.tar.gz -C /var/www/hr-system 2>/dev/null
sudo chown -R www-data:www-data /var/www/hr-system
sudo systemctl start hr-system
sleep 3
echo "── Status ──"
sudo systemctl is-active hr-system
ls -la /var/www/hr-system/hr-system.dll | awk '{print $6,$7,$8,$9}'
REMOTE

echo ""
echo "✅ Deployment erfolgreich. App unter https://test.hr-srgmbh.ch"
