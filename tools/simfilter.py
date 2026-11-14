"""Replays a pointer trace through filter variants: wobble while holding still vs lag while moving."""
import csv, math, sys

R = [{k: float(v) for k, v in r.items()} for r in csv.DictReader(open(sys.argv[1]), delimiter=';')]
T = [r['t_ms'] / 1000 for r in R]; X = [r['raw_x'] for r in R]; Y = [r['raw_y'] for r in R]
W = 3840


def sim(minc, beta, dc, rad_pct, adaptive, v_still=150, hold=0.15, dissolve=0.08):
    out = []; t0 = None; R0 = rad_pct / 100 * W
    for t, px, py in zip(T, X, Y):
        if t0 is None or t - t0 > 0.1:
            x, y, sp = px, py, 0.0; sx, sy = px, py; rad = R0; moving = False; still_t = 0.0; fv = 0.0
        else:
            dt = t - t0
            a = lambda c: 1 / (1 + 1 / (2 * math.pi * c) / dt)
            v = math.hypot(px - x, py - y) / dt; sp += a(dc) * (v - sp)
            k = a(minc + beta * sp); nx = x + k * (px - x); ny = y + k * (py - y)
            fv += a(3.0) * (math.hypot(nx - x, ny - y) / dt - fv)  # speed of the filtered position
            x, y = nx, ny
            if adaptive:
                if not moving and math.hypot(x - sx, y - sy) > rad: moving = True
                if moving:
                    rad = max(0.0, rad - R0 * dt / dissolve)
                    still_t = still_t + dt if fv < v_still else 0.0
                    if still_t > hold: moving = False; rad = R0; still_t = 0.0
            d = math.hypot(x - sx, y - sy)
            if d > rad: f = (d - rad) / d; sx += (x - sx) * f; sy += (y - sy) * f
        t0 = t; out.append((sx, sy))
    return out


win = []; i = 0
while i < len(R) - 50:
    j = i + 50; win.append((i, j, math.hypot(X[j] - X[i], Y[j] - Y[i]) / (T[j] - T[i]))); i = j
still = [(i, j) for i, j, n in win if n < 150]
med = [(i, j) for i, j, n in win if 150 <= n < 1500]
fast = [(i, j) for i, j, n in win if n >= 1500]


def metrics(out):
    jit = 0.0; tt = 0.0
    for i, j in still:
        for s in range(i, j - 10, 10):  # wobble = travel minus straight displacement per 100 ms
            trav = sum(math.hypot(out[k + 1][0] - out[k][0], out[k + 1][1] - out[k][1]) for k in range(s, s + 10))
            jit += trav - math.hypot(out[s + 10][0] - out[s][0], out[s + 10][1] - out[s][1])
        tt += T[j] - T[i]
    lag = lambda ws: sum(math.hypot(out[k][0] - X[k], out[k][1] - Y[k]) for i, j in ws for k in range(i, j)) / sum(j - i for i, j in ws)
    return jit / tt, lag(med), lag(fast)


print("%-30s wobble %5.1f px/s | lag slow %5.1f px | lag fast %5.1f px" % ("raw", *metrics(list(zip(X, Y)))))
cases = [
    ("CURRENT fixed r0.25 b.02", (1.0, 0.02, 2.0, 0.25, False)),
    ("fixed r0.40 b.04", (1.0, 0.04, 2.0, 0.40, False)),
    ("adaptive r0.40 b.04", (1.0, 0.04, 2.0, 0.40, True)),
    ("adaptive r0.60 b.04", (1.0, 0.04, 2.0, 0.60, True)),
    ("adaptive r0.60 b.04 vs100", (1.0, 0.04, 2.0, 0.60, True, 100)),
    ("adaptive r0.60 b.04 vs250", (1.0, 0.04, 2.0, 0.60, True, 250)),
    ("adaptive r0.60 b.08", (1.0, 0.08, 2.0, 0.60, True)),
    ("adaptive r0.80 b.04", (1.0, 0.04, 2.0, 0.80, True)),
]
for name, p in cases:
    print("%-30s wobble %5.1f px/s | lag slow %5.1f px | lag fast %5.1f px" % (name, *metrics(sim(*p))))
