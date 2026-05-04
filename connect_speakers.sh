#!/bin/bash
# 等后端 JACK 端口注册完成
sleep 2

BACKEND="SonicARrayBackend"
DEVICE="alsa_output.usb-KLARK_TEKNIK_KT-USB_2CBD139B-00.multichannel-output"

for id in $(seq 1 15); do
    aux=$((id - 1))
    pw-link "${BACKEND}:spk_${id}" "${DEVICE}:playback_AUX${aux}"
    echo "Connected spk_${id} → playback_AUX${aux}"
done
