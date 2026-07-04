#!/usr/bin/env bash
# Сборка в Release и прогон полной матрицы → bench/results/results.csv.
# Доп. аргументы пробрасываются в оркестратор, напр.:
#   scripts/run.sh --max-cells 1            # быстрый smoke (одна ячейка)
#   scripts/run.sh --conns 128 --dur 8      # другая ось нагрузки
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet build -c Release
dotnet run -c Release --no-build --project src/EchoBench.Orchestrator -- "$@"
