# memory.gp — ФЛАГМАН: working set (MB) от числа соединений.
# Линия на каждую (модель × режим GC); пулинг фиксирован (pooled=1). Данные: plot_memory.dat
# (X=conns, далее 8 колонок ws_mb в порядке MemSeries из PlotRunner). Запуск из корня репо.
load "bench/plots/common.gp"

set output "bench/results/img/memory.png"

set title "Working set от числа соединений  (флагман; pooled=1)" font 'Sans,13'
set xlabel "соединения (log)"
set ylabel "working set, MB"
set logscale x 2
set xtics (64, 128, 256, 512, 1024, 2048, 4096, 8192)
set key top left

FILE = "bench/results/plot_memory.dat"

# Порядок и подписи серий ДОЛЖНЫ совпадать с MemSeries в PlotRunner.cs.
array T[8] = [ "TPC WS/blk", "TPC WS/bg", "TPC Srv/blk", "TPC Srv/bg", \
               "Async WS/blk", "Async WS/bg", "Async Srv/blk", "Async Srv/bg" ]

plot for [i=1:8] FILE using 1:(column(i+1)) with linespoints ls i title T[i]
