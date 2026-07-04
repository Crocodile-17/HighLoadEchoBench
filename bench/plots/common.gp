# common.gp — общие настройки gnuplot для всех графиков стенда.
# Подключается остальными скриптами первой строкой:  load "bench/plots/common.gp"
#
# КОНВЕНЦИЯ ПУТЕЙ (важно): gnuplot резолвит относительные пути от ТЕКУЩЕГО каталога (CWD),
# а НЕ от расположения скрипта. Поэтому все .gp писаны от КОРНЯ репозитория и запускаются
# из него:
#     cd <repo-root> && gnuplot bench/plots/memory.gp
# Оркестратор (PlotRunner) запускает gnuplot ровно так же: cwd = корень репо. Данные читаются
# из bench/results/plot_*.dat (их пишет PlotRunner), PNG кладутся в bench/results/img/.
#
# Формат .dat: пробел-разделённые колонки, строковые метки — в "кавычках" (xtic), комментарии — '#'.
# Поэтому НЕ ставим datafile separator "," — разделитель по умолчанию (пробелы) то, что нужно.

set datafile missing "NaN"          # пропуски (нет ячейки) → разрыв линии, не ноль

# --- Терминал и вывод -------------------------------------------------------
set terminal pngcairo size 1000,640 enhanced font 'Sans,11' background rgb 'white'
# `set output` задаёт каждый скрипт сам (имя PNG своё).

# --- Общий стиль ------------------------------------------------------------
set grid back lc rgb '#d0d0d0' lw 1
set border 3 lc rgb '#404040'           # только левая+нижняя оси
set tics nomirror out
set key box opaque samplen 2 spacing 1.1

# --- Палитра серий (tab10) — 8 стилей: 1-4 ThreadPerConnection, 5-8 Async ----
set style line 1 lc rgb '#1f77b4' lw 2 pt 7 ps 1.2
set style line 2 lc rgb '#ff7f0e' lw 2 pt 7 ps 1.2
set style line 3 lc rgb '#2ca02c' lw 2 pt 7 ps 1.2
set style line 4 lc rgb '#d62728' lw 2 pt 7 ps 1.2
set style line 5 lc rgb '#9467bd' lw 2 pt 5 ps 1.2
set style line 6 lc rgb '#8c564b' lw 2 pt 5 ps 1.2
set style line 7 lc rgb '#e377c2' lw 2 pt 5 ps 1.2
set style line 8 lc rgb '#17becf' lw 2 pt 5 ps 1.2

# Для кластерных гистограмм: модель TPC = синий (ls 1), Async = фиолетовый (ls 5).
