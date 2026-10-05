"""Generates EEG_Band_Ratios.svg (A4 portrait) and converts it to PDF with PyMuPDF."""
import sys, pathlib
from xml.sax.saxutils import escape
import fitz

OUT = pathlib.Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)

W, H = 794, 1123          # A4 @ 96 dpi
L, R = 60, 734            # content left/right
FONT = "Helvetica, Arial, sans-serif"
INK, MUTED, FAINT, RULE = "#1d2129", "#5b6270", "#8a909c", "#d9dce2"
ACCENT = "#2f8f5b"

# Band colours match EegSignalOverlay.BandColors (darkened for print).
BANDS = [  # sym, name, lo, hi, colour
    ("δ", "Delta", 1, 4, "#8a6cf0"),
    ("θ", "Theta", 4, 8, "#3f8fe0"),
    ("α", "Alpha", 8, 12, "#2fb57e"),
    ("βl", "Beta low", 12, 16, "#b9b52a"),
    ("βm", "Beta mid", 16, 20, "#e0962e"),
    ("βh", "Beta high", 20, 30, "#e0603e"),
    ("γ", "Gamma", 30, 45, "#c94aa8"),
]

out = []
def t(x, y, s, size=11, fill=INK, weight="normal", anchor="start", style="normal", family=FONT):
    out.append(f'<text x="{x}" y="{y}" font-family="{family}" font-size="{size}" fill="{fill}" '
               f'font-weight="{weight}" font-style="{style}" text-anchor="{anchor}">{escape(s)}</text>')
def rect(x, y, w, h, fill, rx=0, stroke="none", sw=1):
    out.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>')
def line(x1, y1, x2, y2, stroke=RULE, sw=1):
    out.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{stroke}" stroke-width="{sw}"/>')

rect(0, 0, W, H, "#ffffff")

# ---------------------------------------------------------------- header
t(L, 62, "EEG band-power ratios", 26, weight="bold")
t(L, 86, "What each relaxation metric measures, and how it drives the tree", 13, MUTED)
t(R, 62, "oak_bci · Unicorn Hybrid Black", 10, FAINT, anchor="end")
t(R, 76, "8 channels averaged · 2026-10-05", 10, FAINT, anchor="end")
line(L, 104, R, 104, INK, 1.5)

# ---------------------------------------------------------------- 1. band strip
t(L, 132, "1  THE FREQUENCY BANDS", 11, ACCENT, "bold")
x0, x1, fmax = L, R, 45.0
X = lambda hz: x0 + (x1 - x0) * hz / fmax
sy, sh = 168, 40
# beta brace
bx0, bx1 = X(12), X(30)
out.append(f'<path d="M{bx0} {sy-4} V{sy-10} H{bx1} V{sy-4}" fill="none" stroke="{MUTED}" stroke-width="1"/>')
t((bx0 + bx1) / 2, sy - 14, "β = βlow + βmid + βhigh  (12–30 Hz)", 10, MUTED, anchor="middle")
for sym, name, lo, hi, col in BANDS:
    rect(X(lo), sy, X(hi) - X(lo), sh, col)
    out.append(f'<line x1="{X(hi)}" y1="{sy}" x2="{X(hi)}" y2="{sy+sh}" stroke="#ffffff" stroke-width="1.5"/>')
    t((X(lo) + X(hi)) / 2, sy + 25, sym, 15 if len(sym) == 1 else 12, "#ffffff", "bold", "middle")
rect(X(0), sy, X(1) - X(0), sh, "#eceef1")
# axis
ay = sy + sh + 4
for hz in range(0, 46, 5):
    line(X(hz), ay, X(hz), ay + 5, MUTED)
    t(X(hz), ay + 17, f"{hz}", 9, MUTED, anchor="middle")
t(R, ay + 30, "Hz  (γ stops at 45 Hz to stay clear of 50 Hz mains)", 9, FAINT, anchor="end")

# band cards
cy, cw, gap = 262, 126, 11
CARDS = [
    ("δ", "Delta", "1–4 Hz", "#8a6cf0", ["Deep sleep.", "Awake: mostly blink", "& movement artifact."]),
    ("θ", "Theta", "4–8 Hz", "#3f8fe0", ["Drowsiness, deep", "meditation, memory.", "Frontal θ = focus."]),
    ("α", "Alpha", "8–12 Hz", "#2fb57e", ["Relaxed wakefulness;", "strongest with eyes", "closed, at the back."]),
    ("β", "Beta", "12–30 Hz", "#e0603e", ["Alert, busy mind,", "mental effort,", "muscle tension."]),
    ("γ", "Gamma", "30–45 Hz", "#c94aa8", ["Cognitive binding;", "on scalp EEG mostly", "muscle artifact."]),
]
for i, (sym, name, rng, col, lines) in enumerate(CARDS):
    cx = L + i * (cw + gap)
    rect(cx, cy, cw, 92, "#f6f7f9", 6)
    rect(cx, cy, 4, 92, col)
    t(cx + 12, cy + 24, sym, 20, col, "bold")
    t(cx + 34, cy + 17, name, 11, INK, "bold")
    t(cx + 34, cy + 29, rng, 9.5, MUTED)
    for j, s in enumerate(lines):
        t(cx + 12, cy + 50 + j * 13, s, 9.5, INK)

# ---------------------------------------------------------------- 2. metric table
ty = 378
t(L, ty, "2  THE EIGHT METRICS  (keys 1–8 in the HUD)", 11, ACCENT, "bold")
hy = ty + 24
t(L + 4, hy, "#", 9, FAINT, "bold")
t(L + 34, hy, "RATIO", 9, FAINT, "bold")
t(L + 194, hy, "WHEN RELAXING", 9, FAINT, "bold")
t(L + 294, hy, "WHAT IT MEANS", 9, FAINT, "bold")
line(L, hy + 7, R, hy + 7, INK, 1)

METRICS = [  # formula, name, badge, badge sub, meaning, caveat, highlight
    ("α / β", "Alpha / Beta", "RISES", "",
     "Relaxed vs. engaged: alpha grows and beta falls as effort and tension drop.",
     "Simple and intuitive; closing the eyes alone boosts alpha strongly.", False),
    ("(α + θ) / β", "(Alpha + Theta) / Beta", "RISES", "default",
     "Relaxation deepening toward meditation: calm alpha and slow theta over beta.",
     "Classic alpha–theta neurofeedback ratio; also rises if the user gets drowsy.", True),
    ("α / total", "Relative Alpha", "RISES", "",
     "Share of all EEG power that is alpha; ignores overall amplitude (contact, gain).",
     "Blinks and movement add delta to the total and pull it down.", False),
    ("α / (θ + β)", "Alpha / (Theta + Beta)", "RISES", "",
     "Calm but awake: rewards alpha, penalises both tension (β) and drowsiness (θ).",
     "Strictest score; use when falling asleep must not count as relaxing.", False),
    ("(α + 0.5θ) / β", "Relaxation Score", "RISES", "",
     "Middle ground between 1 and 2: theta counts half, so drowsiness helps less.",
     "The 0.5 weighting is a heuristic, not a published standard.", False),
    ("θ at Fz", "Frontal Theta", "RISES", "with focus",
     "Frontal-midline theta: marker of sustained inward attention (focused meditation).",
     "Absolute power at one electrode, not a ratio: sensitive to contact quality.", False),
    ("θ / α", "Theta / Alpha", "RISES", "when drowsy",
     "Shift from relaxed wakefulness toward sleep onset or very deep meditation.",
     "Cannot tell deep meditation from dozing off; best as a drowsiness monitor.", False),
    ("θ / β", "Theta / Beta", "MIXED", "",
     "Classic attention marker (high = low alert arousal), used in ADHD neurofeedback.",
     "Not meditation-specific; mixes relaxation with inattention.", False),
]
rh = 45
for i, (formula, name, badge, sub, meaning, caveat, hl) in enumerate(METRICS):
    ry = hy + 10 + i * rh
    if hl:
        rect(L, ry, R - L, rh, "#eaf6ef")
    elif i % 2 == 1:
        rect(L, ry, R - L, rh, "#f8f9fa")
    # number disc
    out.append(f'<circle cx="{L+12}" cy="{ry+rh/2}" r="10" fill="{ACCENT if hl else INK}"/>')
    t(L + 12, ry + rh / 2 + 4, str(i + 1), 11, "#ffffff", "bold", "middle")
    t(L + 34, ry + 21, formula, 15, INK, "bold")
    t(L + 34, ry + 35, name, 9.5, MUTED)
    bcol = ACCENT if badge == "RISES" else "#b0782a"
    rect(L + 194, ry + 11, 52, 16, bcol, 8)
    t(L + 220, ry + 22.5, badge, 8.5, "#ffffff", "bold", "middle")
    if sub:
        t(L + 220, ry + 38, sub, 8.5, ACCENT if hl else MUTED, "bold" if hl else "normal", "middle")
    t(L + 294, ry + 19, meaning, 10, INK)
    t(L + 294, ry + 33, caveat, 9.5, MUTED, style="italic")
    line(L, ry + rh, R, ry + rh, RULE)

# ---------------------------------------------------------------- 3. pipeline
py = hy + 10 + 8 * rh + 38
t(L, py, "3  FROM SIGNAL TO TREE", 11, ACCENT, "bold")
STEPS = [
    ("Raw EEG", ["8 ch · 250 Hz", "µV"]),
    ("FFT", ["2 s window", "10 × per s"]),
    ("Band power", ["δ … γ, mean", "of 8 channels"]),
    ("Ratio", ["selected", "metric 1–8"]),
    ("z-score", ["vs. own 30 s", "rest baseline"]),
    ("Relaxation", ["z -0.5 → 0", "z +2 → 1"]),
    ("Tree growth", ["grows above 0.25", "pauses below"]),
]
bw, bg = 84, 14
by = py + 16
for i, (head, lines) in enumerate(STEPS):
    bx = L + i * (bw + bg)
    last = i == len(STEPS) - 1
    rect(bx, by, bw, 62, "#eaf6ef" if last else "#f6f7f9", 6, ACCENT if last else RULE)
    t(bx + bw / 2, by + 20, head, 10.5, INK, "bold", "middle")
    for j, s in enumerate(lines):
        t(bx + bw / 2, by + 36 + j * 12, s, 8.5, MUTED, anchor="middle")
    if not last:
        ax = bx + bw + 2
        out.append(f'<path d="M{ax} {by+31} h8 m-4 -4 l4 4 l-4 4" fill="none" stroke="{MUTED}" stroke-width="1.3"/>')
t(L, by + 80, "Smoothed over 1.5 s and blended over 0.75 s when switching metric, so the tree never jumps.", 9, FAINT)

# ---------------------------------------------------------------- 4. reading the numbers
ny = by + 112
t(L, ny, "4  READING THE NUMBERS", 11, ACCENT, "bold")
NOTES = [
    ("Only relative values count.", "Absolute ratios differ between people, sessions and electrode contact; that is why every metric is z-scored against the participant's own baseline."),
    ("Artifacts mimic states.", "Eyes closed → α up. Blinks or head movement → δ up. Jaw clench or frown → β and γ up (looks like tension). Poor contact → flat line or 50 Hz hum."),
    ("Picking a metric.", "Start with 2, (α+θ)/β. Switch to 4 if participants drift off to sleep, or to 6 for focused-attention meditation."),
]
yy = ny + 22
for head, body in NOTES:
    out.append(f'<circle cx="{L+4}" cy="{yy-4}" r="2.5" fill="{ACCENT}"/>')
    words, cur, rows = body.split(), "", []
    limit = 118 - len(head)
    for w in words:
        if len(cur) + len(w) + 1 > limit:
            rows.append(cur); cur = w; limit = 122
        else:
            cur = (cur + " " + w).strip()
    rows.append(cur)
    out.append(f'<text x="{L+14}" y="{yy}" font-family="{FONT}" font-size="10" fill="{MUTED}">'
               f'<tspan font-weight="bold" fill="{INK}">{escape(head)}</tspan> {escape(rows[0])}</text>')
    for k, r in enumerate(rows[1:], 1):
        t(L + 14, yy + k * 14, r, 10, MUTED)
    yy += 14 * len(rows) + 9

line(L, H - 44, R, H - 44, RULE)
t(L, H - 28, "Source: Assets/Relaxation/RelaxationMetrics.cs · RawEegProcessor.cs · RelaxationTreeDriver.cs", 8.5, FAINT)
t(R, H - 28, "MyHackathonHorror", 8.5, FAINT, anchor="end")

svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="210mm" height="297mm" viewBox="0 0 {W} {H}">\n'
       + "\n".join(out) + "\n</svg>\n")
svg_path = OUT / "EEG_Band_Ratios.svg"
svg_path.write_text(svg, encoding="utf-8")

src = fitz.open(str(svg_path))
pdf = fitz.open("pdf", src.convert_to_pdf())
pdf.save(str(OUT / "EEG_Band_Ratios.pdf"))
pdf[0].get_pixmap(dpi=110).save(str(pathlib.Path(sys.argv[2]) / "preview.png"))
print("pages:", pdf.page_count, "size:", pdf[0].rect)
