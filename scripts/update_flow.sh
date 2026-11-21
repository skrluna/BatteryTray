#!/bin/bash
cd /d/battery || exit 1
rm -f swap_status.txt
echo '[1/4] waiting for UAC approval...'
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','D:\\battery\\UpdateSwap.ps1'" || { echo 'UAC_CANCELLED'; exit 2; }
echo '[2/4] waiting for elevated swap (max 90s)...'
n=0
while [ $n -lt 90 ]; do [ -f swap_status.txt ] && break; sleep 1; n=$((n+1)); done
if [ ! -f swap_status.txt ]; then echo 'SWAP_TIMEOUT'; exit 3; fi
cat swap_status.txt
grep -q STARTED swap_status.txt || exit 4
echo '[3/4] new exe running, waiting for tray registration...'
sleep 8
echo '[4/4] verifying...'
powershell -NoProfile -ExecutionPolicy Bypass -File CheckState.ps1
echo FLOW_DONE