#!/usr/bin/env bash
# Ставит инструменты стенда. Метрики GC/памяти снимаются ВНЕ процесса Host через
# .NET diagnostic global tools (ARCHITECTURE §4) — без них оркестратор не соберёт счётчики.
set -euo pipefail

# Внешний сбор счётчиков (обязателен для оркестратора) + снимок кучи (опц., стретч).
dotnet tool install -g dotnet-counters 2>/dev/null || dotnet tool update -g dotnet-counters
dotnet tool install -g dotnet-gcdump   2>/dev/null || dotnet tool update -g dotnet-gcdump

# Графики (шаг 8): нужен gnuplot с терминалом pngcairo. Способ по правам:
#   с root:  sudo apt-get install -y gnuplot-nox
#   без root: качаем .deb и распаковываем в ~/.local (системные cairo/pango уже есть) —
#             см. install_gnuplot_local ниже.
install_gnuplot_local() {
  local tmp dest bin
  tmp="$(mktemp -d)"; dest="$HOME/.local/opt/gnuplot-nox"; bin="$HOME/.local/bin"
  mkdir -p "$dest" "$bin"
  ( cd "$tmp" && apt-get download gnuplot-nox && dpkg -x gnuplot-nox_*.deb x )
  cp -r "$tmp/x/usr" "$dest/"
  cat > "$bin/gnuplot" <<'WRAP'
#!/usr/bin/env bash
export GNUPLOT_LIB="${GNUPLOT_LIB:-$HOME/.local/opt/gnuplot-nox/usr/share/gnuplot/6.0}"
exec "$HOME/.local/opt/gnuplot-nox/usr/bin/gnuplot-nox" "$@"
WRAP
  chmod +x "$bin/gnuplot"; rm -rf "$tmp"
  echo "gnuplot установлен в $bin/gnuplot (без root). Добавь $bin в PATH."
}

if command -v gnuplot >/dev/null; then
  echo "gnuplot найден: $(command -v gnuplot)"
elif command -v sudo >/dev/null && sudo -n true 2>/dev/null; then
  sudo apt-get install -y gnuplot-nox
else
  echo "gnuplot не найден и нет root — ставлю локально в ~/.local…"
  install_gnuplot_local || echo "ВНИМАНИЕ: локальная установка gnuplot не удалась (нужен для шага 8)."
fi
command -v tcpkali  >/dev/null || echo "Инфо: tcpkali не найден (опциональная альтернатива LoadClient)."

echo "Готово. Убедись, что в PATH:  export PATH=\"\$HOME/.dotnet/tools:\$HOME/.local/bin:\$PATH\""
