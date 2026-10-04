#!/usr/bin/env bash
# Samples the server while a load test runs, started and stopped by `lt.sh monitor start|stop`. No agents, nothing to
# install: /proc, docker stats and the load-test MySQL's own counters, every 10 seconds, into monitor/*.csv. Light by
# design (a few processes a sample), so watching doesn't change what it watches.
#
#   host.csv        cpu %, iowait %, load, memory and swap used, disk and network throughput, open TCP connections
#   containers.csv  every container's CPU and memory, production's included (they share the server)
#   mysql.csv       the load-test MySQL: connections, running threads, queries, slow queries, row locks, deadlocks
set -uo pipefail

here=$(cd "$(dirname "$0")" && pwd)
out="$here/monitor"
interval=10
mkdir -p "$out"

root_password=$(grep -m1 '^LT_DB_ROOT_PASSWORD=' "$here/.env" | cut -d= -f2-)
db=$(docker compose -p idi-loadtest ps -q lt-db 2>/dev/null)

cpu_counters() { awk '/^cpu /{print $2+$3+$4+$5+$6+$7+$8+$9, $5, $6}' /proc/stat; }
disk_counters() { awk '$3 ~ /^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme[0-9]+n[0-9]+)$/ {r+=$6; w+=$10; t+=$13} END {print r+0, w+0, t+0}' /proc/diskstats; }
net_counters() { awk -F'[: ]+' 'NR>2 && $2 !~ /^(lo|docker|br-|veth)/ {rx+=$3; tx+=$11} END {print rx+0, tx+0}' /proc/net/dev; }

mysql_sample() {
    docker exec -e MYSQL_PWD="$root_password" "$db" mysql -uroot -N -B -e "
      SELECT CONCAT_WS(',',
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Threads_connected'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Threads_running'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Questions'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Slow_queries'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Innodb_row_lock_waits'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Innodb_row_lock_time'),
        (SELECT COUNT FROM information_schema.INNODB_METRICS WHERE NAME = 'lock_deadlocks'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Aborted_connects'),
        (SELECT VARIABLE_VALUE FROM performance_schema.global_status WHERE VARIABLE_NAME = 'Max_used_connections'))" 2>/dev/null
}

echo "ts,cpu_pct,iowait_pct,load1,mem_used_mb,mem_total_mb,swap_used_mb,disk_read_kbs,disk_write_kbs,disk_busy_pct,net_rx_kbs,net_tx_kbs,tcp_established" > "$out/host.csv"
echo "ts,container,cpu_pct,mem_mb" > "$out/containers.csv"
echo "ts,threads_connected,threads_running,questions,slow_queries,row_lock_waits,row_lock_time_ms,deadlocks,aborted_connects,max_used_connections" > "$out/mysql.csv"

read -r cpu_total cpu_idle cpu_wait < <(cpu_counters)
read -r disk_read disk_write disk_busy < <(disk_counters)
read -r net_rx net_tx < <(net_counters)

while true; do
    sleep "$interval"
    ts=$(date +%s)

    read -r total idle wait < <(cpu_counters)
    read -r dr dw dt < <(disk_counters)
    read -r rx tx < <(net_counters)
    load1=$(cut -d' ' -f1 /proc/loadavg)
    read -r mem_total mem_available swap_total swap_free < <(awk '/^MemTotal:/{t=$2} /^MemAvailable:/{a=$2} /^SwapTotal:/{st=$2} /^SwapFree:/{sf=$2} END{print t, a, st, sf}' /proc/meminfo)
    established=$(ss -Htan state established 2>/dev/null | wc -l)
    awk -v dt_total=$((total - cpu_total)) -v dt_idle=$((idle - cpu_idle)) -v dt_wait=$((wait - cpu_wait)) \
        -v load1="$load1" -v mt="$mem_total" -v ma="$mem_available" -v st="$swap_total" -v sf="$swap_free" \
        -v dr=$((dr - disk_read)) -v dw=$((dw - disk_write)) -v db=$((dt - disk_busy)) -v rx=$((rx - net_rx)) -v tx=$((tx - net_tx)) \
        -v i="$interval" -v ts="$ts" -v est="$established" 'BEGIN {
          busy = dt_total > 0 ? 100 * (dt_total - dt_idle - dt_wait) / dt_total : 0
          iow = dt_total > 0 ? 100 * dt_wait / dt_total : 0
          printf "%d,%.1f,%.1f,%s,%d,%d,%d,%.0f,%.0f,%.1f,%.0f,%.0f,%d\n", ts, busy, iow, load1, (mt - ma) / 1024, mt / 1024, (st - sf) / 1024,
            dr * 512 / 1024 / i, dw * 512 / 1024 / i, 100 * db / (i * 1000), rx / 1024 / i, tx / 1024 / i, est
        }' >> "$out/host.csv"
    cpu_total=$total; cpu_idle=$idle; cpu_wait=$wait
    disk_read=$dr; disk_write=$dw; disk_busy=$dt
    net_rx=$rx; net_tx=$tx

    docker stats --no-stream --format '{{.Name}},{{.CPUPerc}},{{.MemUsage}}' 2>/dev/null | awk -F, -v ts="$ts" '{
        cpu = $2; sub(/%/, "", cpu)
        split($3, mem, " / "); value = mem[1]; unit = value; gsub(/[0-9.]/, "", unit); gsub(/[A-Za-z]/, "", value)
        mb = unit == "GiB" ? value * 1024 : unit == "KiB" ? value / 1024 : unit == "B" ? value / 1048576 : value
        printf "%s,%s,%s,%.0f\n", ts, $1, cpu, mb
    }' >> "$out/containers.csv"

    if [ -n "$db" ] && sample=$(mysql_sample) && [ -n "$sample" ]; then
        echo "$ts,$sample" >> "$out/mysql.csv"
    fi
done
