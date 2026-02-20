import time, math, argparse
from pythonosc.udp_client import SimpleUDPClient

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ip", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=7000)
    ap.add_argument("--n", type=int, default=4)          # 2~8
    ap.add_argument("--hz", type=float, default=60.0)    # 30/60/90
    ap.add_argument("--addr", default="/spatial/source_pos")
    ap.add_argument("--with_id", action="store_true")    # 如果你的接收需要 id，就加这个参数
    args = ap.parse_args()

    c = SimpleUDPClient(args.ip, args.port)
    dt = 1.0 / args.hz
    t0 = time.time()

    print(f"Sending OSC to {args.ip}:{args.port} addr={args.addr} sources={args.n} hz={args.hz} with_id={args.with_id}")
    while True:
        t = time.time() - t0
        for sid in range(args.n):
            phase = 2.0 * math.pi * sid / max(1, args.n)
            ang = 2.0 * math.pi * 0.10 * t + phase
            x = math.cos(ang)
            z = math.sin(ang)
            y = 0.3 * math.sin(2.0 * math.pi * 0.07 * t + phase)

            # normalize
            norm = math.sqrt(x*x + y*y + z*z)
            if norm > 1e-6:
                x, y, z = x/norm, y/norm, z/norm

            if args.with_id:
                # 常见： (int id, float x, float y, float z)
                c.send_message(args.addr, [sid, float(x), float(y), float(z)])
            else:
                # 另一种： (float x, float y, float z) 默认 source 0
                c.send_message(args.addr, [float(x), float(y), float(z)])

        time.sleep(dt)

if __name__ == "__main__":
    main()
