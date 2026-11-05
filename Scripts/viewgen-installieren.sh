#!/bin/bash
# Legt Swissdec Viewgen (Java) und das JDK nach /opt/viewgen. Auf dem Server ausführen.
# Einmalig pro Viewgen-Version, ein Deploy fasst /opt/viewgen nicht an.
#
#   sudo bash viewgen-installieren.sh <viewgen-java-….zip> <jdk-Ordner>
#
# Viewgen bleibt unverändert (Lizenzbestimmungen Ziffer 4). OneCrew liest den Ordner nur;
# der Bericht entsteht im Temp-Ordner des Dienstes. Kein Neustart der Dienste nötig.
set -euo pipefail

ZIP="${1:?Pfad zum Viewgen-Java-Zip fehlt}"
JDK="${2:?Pfad zum JDK-Ordner fehlt}"
test -x "$JDK/bin/java"

ZIEL=/opt/viewgen
NEU=$(mktemp -d /opt/viewgen.neu.XXXX)
trap 'rm -rf "$NEU"' EXIT

mkdir "$NEU/app.tmp"
unzip -q "$ZIP" -d "$NEU/app.tmp"
mv "$NEU"/app.tmp/viewgen-java-* "$NEU/app"
rmdir "$NEU/app.tmp"
cp -a "$JDK" "$NEU/jdk"

chown -R root:www-data "$NEU"
chmod -R g+rX,o-rwx "$NEU"
chmod 750 "$NEU"

rm -rf "$ZIEL.alt"
if [ -d "$ZIEL" ]; then mv "$ZIEL" "$ZIEL.alt"; fi
mv "$NEU" "$ZIEL"
trap - EXIT
rm -rf "$ZIEL.alt"

sudo -u www-data "$ZIEL/jdk/bin/java" -version 2>&1 | head -1
ls "$ZIEL/app/lib" | grep '^viewgen-java'
