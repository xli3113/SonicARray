import math, time
from pythonosc.udp_client import SimpleUDPClient

IP = "127.0.0.1"
PORT = 7000

src_id = 0
R = 1.0
Y = 0.0
rev_per_sec = 0.2   # 5秒一圈
hz = 60

client = SimpleUDPClient(IP, PORT)
t0 = time.time()

while True:
    t = time.time() - t0
    theta = 2 * math.pi * rev_per_sec * t

    # 顺时针（从上往下看）：x=R*sin(theta), z=R*cos(theta)
    x = R * math.cos(theta)
    y = R * math.sin(theta)
    z = 0.0

    # 选一种发送方式（根据你后端实际解析）
    client.send_message("/spatial/source_pos", [src_id, float(x), float(Y), float(z)])
    # 或者：
    # client.send_message(f"/spatial/source_pos/{src_id}", [float(x), float(Y), float(z)])

    time.sleep(1.0 / hz)
