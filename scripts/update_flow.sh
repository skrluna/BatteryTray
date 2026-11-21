#!/bin/bash
cd /d/battery || exit 1
rm -f swap_status.txt
echo '[1/4] waiting for UAC approval...'
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','D:\\battery\\scripts\\UpdateSwap.ps1'" || { echo 'UAC_CANCELLED'; exit 2; }
echo '[2/4] waiting for elevated swap (max 90s)...'
n=0
ok=0
while [ $n -lt 90 ]; do if [ -f swap_status.txt ] && grep -q STARTED swap_status.txt 2>/dev/null; then ok=1; break; fi; sleep 1; n=$((n+1)); done
if [ $ok -ne 1 ]; then [ -f swap_status.txt ] && cat swap_status.txt; echo 'SWAP_TIMEOUT'; exit 3; fi
echo '[3/4] new exe running, waiting for tray registration...'
sleep 8
echo '[4/4] verifying...'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/CheckState.ps1
echo FLOW_DONE
