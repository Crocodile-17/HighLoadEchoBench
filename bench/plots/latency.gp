# latency.gp — перцентили латентности RTT по моделям.
# Кластерная гистограмма: X = перцентили (p50/p95/p99/p999), кластеры = модели.
# Лог-шкала Y, т.к. p999 >> p50 (хвост). Срез: макс. нагрузка (refConns), GC=Server+background,
# pooled=1. Данные: plot_latency.dat (col1=перцентиль [xtic], col2=TPC, col3=Async). Запуск из корня.
load "bench/plots/common.gp"

set output "bench/results/img/latency.png"

set title "Латентность RTT по перцентилям и моделям  (макс. нагрузка, GC=Server+bg, pooled=1)" font 'Sans,13'
set xlabel "перцентиль"
set ylabel "латентность RTT, µs (log)"
set logscale y
set yrange [50:*]
set style data histograms
set style histogram clustered gap 2
set style fill solid 0.85 border lc rgb '#404040'
set boxwidth 0.9 relative
set key top left

FILE = "bench/results/plot_latency.dat"

plot FILE using 2:xtic(1) title "ThreadPerConnection" ls 1, \
     ''   using 3          title "Async"               ls 5
