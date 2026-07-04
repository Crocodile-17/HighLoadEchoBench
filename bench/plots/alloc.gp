# alloc.gp — allocation rate: пул буферов против без пула.
# Кластерная гистограмма: X = модели, кластеры = стратегия буфера (new[] против ArrayPool).
# Срез: макс. нагрузка (refConns), GC=Server+background. Данные: plot_alloc.dat
# (col1=модель [xtic], col2=new[]/без пула, col3=ArrayPool/пул). Запуск из корня репо.
load "bench/plots/common.gp"

set output "bench/results/img/alloc.png"

set title "Allocation rate: пул буферов против без пула  (макс. нагрузка, GC=Server+bg)" font 'Sans,13'
set xlabel "модель"
set ylabel "allocation rate, MB/s"
set yrange [0:*]
set style data histograms
set style histogram clustered gap 2
set style fill solid 0.85 border lc rgb '#404040'
set boxwidth 0.9 relative
set key top left

FILE = "bench/results/plot_alloc.dat"

plot FILE using 2:xtic(1) title "new[] (без пула)"     ls 4, \
     ''   using 3          title "ArrayPool (пул)"      ls 3
