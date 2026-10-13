#!/usr/bin/env bash
# Renders every PlantUML diagram in docs/diagrams/architecture to SVG.
# Uses PlantUML's built-in Smetana layout, so Graphviz is not required.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dir="$root/docs/diagrams/architecture"
diagrams=()
for f in "$dir"/*.plantuml; do
    [[ "$(basename "$f")" == include.plantuml ]] || diagrams+=("$f")
done
java -jar "$root/scripts/tools/plantuml.jar" -Playout=smetana -tsvg "${diagrams[@]}"
