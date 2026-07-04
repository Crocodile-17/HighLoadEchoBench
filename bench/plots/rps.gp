# rps.gp — RPS по моделям, сгруппировано по режиму GC.
# Кластерная гистограмма: X = 4 режима GC (Workstation/Server × blocking/background),
# кластеры = модели. Сравнение на максимальной нагрузке (refConns), pooled=1.
# Данные: plot_rps.dat (col1=режим GC [xtic], col2=TPC, col3=Async). Запуск из корня репо.
load "bench/plots/common.gp"

set output "bench/results/img/rps.png"

set title "RPS по моделям, сгруппировано по режиму GC  (макс. нагрузка, pooled=1)" font 'Sans,13'
set xlabel "режим GC"
set ylabel "RPS, запросов/с"
set yrange [0:*]
set style data histograms
set style histogram clustered gap 2
set style fill solid 0.85 border lc rgb '#404040'
set boxwidth 0.9 relative
set key top right

FILE = "bench/results/plot_rps.dat"

plot FILE using 2:xtic(1) title "ThreadPerConnection" ls 1, \
     ''   using 3          title "Async"               ls 5
