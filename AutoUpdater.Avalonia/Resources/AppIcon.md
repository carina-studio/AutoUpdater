# AppUpdater — Application Icon Design Concept

**Carina Studio · AppSuite**

---

## 1. Overview

This document describes the visual design concept for the AppUpdater application icon — an auto-updater utility that downloads and applies updates to other Carina Studio apps. The design follows the same suite-wide icon language established for ULogViewer, adapted for AppUpdater's blue accent colour and distinct purpose.

The icon concept uses a rocket silhouette as the central metaphor: a rocket launch visually communicates the act of propelling an application into a new version. The warm exhaust flame contrasts with the cool blue background to create immediate visual energy, while the clean white body ensures legibility at all sizes.

---

## 2. Design Philosophy

### Suite consistency

All Carina Studio app icons share the same structural DNA: a solid-colour or gradient background, a single bold illustration centred on the canvas, and no text. When multiple icons appear together in Launchpad, the taskbar, or Finder, they form a coherent family distinguished by illustration and accent colour rather than shape or typography.

### Metaphor

AppUpdater's core function is downloading a new version of another app and replacing it. The rocket metaphor conveys this in a single glance:

- The upward launch direction signals progress and delivery.
- The exhaust flame provides energy and motion without requiring animation.
- The porthole window gives the rocket a sense of payload — the new version being carried.

### Colour strategy

The icon uses a single blue ramp for the background gradient, matching AppUpdater's accent colour (`#006CBF`). The rocket illustration uses white, a darker tonal nose cone (`#2E6080`), and deep navy fins (`#003566`) to create four distinct tonal layers without requiring any additional hue. The orange-amber-cream flame is the only warm accent, making it immediately eye-catching against the cool background.

---

## 3. Visual Elements

### 3.1 Background

- **Shape:** rounded rectangle with `rx=128` (Windows/Linux) or square PNG clipped by OS (macOS squircle)
- **Fill:** vertical linear gradient from `#38BDF8` (top) to `#006CBF` (bottom)
- The top-light-to-bottom-dark direction reinforces the upward launch direction

### 3.2 Rocket body

- A tall rounded pill (`rx=124`) in pure white — the dominant shape on the canvas
- Occupies roughly 24% of canvas width, centred horizontally
- Spans from `y=210` to `y=766` (top of body to base), giving generous vertical presence

### 3.3 Nose cone

- A quadratic bezier triangle that caps the top of the rocket body
- Fill: `#2E6080` — darker than the gradient but lighter than the fins, creating a clear mid-tone layer
- Separates visually from the white body without requiring an outline stroke

### 3.4 Fins

- Two triangular fins, one on each side of the body base
- Fill: `#003566` — deep navy, providing maximum contrast against the white body
- Simplified triangle shapes that remain legible at 16px

### 3.5 Porthole window

- **Outer ring:** circle `r=88`, fill `#003566` — matches the fins for visual unity
- **Glass:** circle `r=64`, fill `#7DD3FC` — sky blue, echoing the top of the background gradient
- **Glint:** small circle `r=20` at upper-left of the glass, white at 55% opacity — adds depth

### 3.6 Exhaust flame

Three concentric bezier strokes layered from widest to narrowest, creating a natural flame depth:

- **Outer layer:** stroke-width 56, colour `#F97316` (orange)
- **Mid layer:** stroke-width 38, colour `#FBBF24` (amber)
- **Inner core:** stroke-width 22, colour `#FEF9C3` (pale yellow-white)

At small sizes (32px and below), only the outer and inner layers are rendered to avoid visual noise.

---

## 4. Colour Palette

| Role | Hex | Usage |
|---|---|---|
| Background top | `#38BDF8` | Gradient start — sky blue |
| Background bottom | `#006CBF` | Gradient end — accent blue |
| Rocket body | `#FFFFFF` | Main fuselage and fins |
| Nose cone | `#2E6080` | Mid-blue tint — tonal separation from body |
| Fins & window ring | `#003566` | Dark navy — max contrast against body |
| Window glass | `#7DD3FC` | Sky blue porthole glass |
| Flame outer | `#F97316` | Exhaust — orange |
| Flame mid | `#FBBF24` | Exhaust — amber |
| Flame inner core | `#FEF9C3` | Exhaust — pale yellow-white |

---

## 5. Canvas & Coordinates

All artwork is produced on a **1024 × 1024 px** master canvas. Platform-specific shapes are applied by clipping this master.

| Element | Coordinates / values | Notes |
|---|---|---|
| Canvas | 1024 × 1024 px | Master artwork canvas |
| Background gradient | Top `#38BDF8` → Bottom `#006CBF` | Vertical linear gradient |
| Rocket body | `x=388, y=210, w=248, h=556, rx=124` | Rounded pill — white fill |
| Nose cone | Bezier: `(388,330)` → `(512,76)` → `(636,330)` | Quadratic curves — `#2E6080` |
| Left fin | Points: `388,618 → 240,810 → 388,758` | Triangle — `#003566` |
| Right fin | Points: `636,618 → 784,810 → 636,758` | Triangle — `#003566` |
| Window ring (outer) | `cx=512, cy=448, r=88` | Circle — `#003566` |
| Window glass | `cx=512, cy=448, r=64` | Circle — `#7DD3FC` |
| Window glint | `cx=490, cy=428, r=20` | Circle — white, 55% opacity |
| Flame outer | Bezier from `y=766`, stroke-width 56 | `#F97316`, round linecap |
| Flame mid | Bezier from `y=766`, stroke-width 38 | `#FBBF24`, round linecap |
| Flame inner | Bezier from `y=766`, stroke-width 22 | `#FEF9C3`, round linecap |

### SVG source (master)

```xml
<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="#38bdf8"/>
      <stop offset="100%" stop-color="#006cbf"/>
    </linearGradient>
    <clipPath id="shape">
      <!-- Windows/Linux: pre-applied rx=128 -->
      <rect width="1024" height="1024" rx="128"/>
      <!-- macOS: omit clip, submit square PNG -->
    </clipPath>
  </defs>
  <g clip-path="url(#shape)">
    <!-- Background -->
    <rect width="1024" height="1024" fill="url(#bg)"/>
    <!-- Rocket body -->
    <rect x="388" y="210" width="248" height="556" rx="124" fill="#ffffff"/>
    <!-- Nose cone -->
    <path d="M388 330 Q388 136 512 76 Q636 136 636 330 Z" fill="#2e6080"/>
    <!-- Left fin -->
    <path d="M388 618 L240 810 L388 758 Z" fill="#003566"/>
    <!-- Right fin -->
    <path d="M636 618 L784 810 L636 758 Z" fill="#003566"/>
    <!-- Window ring -->
    <circle cx="512" cy="448" r="88" fill="#003566"/>
    <!-- Window glass -->
    <circle cx="512" cy="448" r="64" fill="#7dd3fc"/>
    <!-- Window glint -->
    <circle cx="490" cy="428" r="20" fill="#ffffff" fill-opacity="0.55"/>
    <!-- Flame outer -->
    <path d="M424 766 Q462 954 512 868 Q562 954 600 766"
          fill="none" stroke="#f97316" stroke-width="56" stroke-linecap="round"/>
    <!-- Flame mid -->
    <path d="M448 766 Q480 900 512 838 Q544 900 576 766"
          fill="none" stroke="#fbbf24" stroke-width="38" stroke-linecap="round"/>
    <!-- Flame inner -->
    <path d="M466 766 Q490 858 512 820 Q534 858 558 766"
          fill="none" stroke="#fef9c3" stroke-width="22" stroke-linecap="round"/>
  </g>
</svg>
```

---

## 6. Platform Specifications

### 6.1 macOS

Submit a **square PNG with no pre-applied clipping**. macOS applies the squircle mask (approximately `rx=22%` of width) automatically at runtime. Applying any clipping before submission causes a visible white border artifact.

Convert the `.iconset` folder to `.icns` with:

```bash
iconutil -c icns AppUpdater.iconset
```

| Filename | Logical size | Scale | Pixel size |
|---|---|---|---|
| `icon_16x16.png` | 16 × 16 | 1× | 16 × 16 |
| `icon_16x16@2x.png` | 16 × 16 | 2× | 32 × 32 |
| `icon_32x32.png` | 32 × 32 | 1× | 32 × 32 |
| `icon_32x32@2x.png` | 32 × 32 | 2× | 64 × 64 |
| `icon_128x128.png` | 128 × 128 | 1× | 128 × 128 |
| `icon_128x128@2x.png` | 128 × 128 | 2× | 256 × 256 |
| `icon_256x256.png` | 256 × 256 | 1× | 256 × 256 |
| `icon_256x256@2x.png` | 256 × 256 | 2× | 512 × 512 |
| `icon_512x512.png` | 512 × 512 | 1× | 512 × 512 |
| `icon_512x512@2x.png` | 512 × 512 | 2× | 1024 × 1024 |

### 6.2 Windows

Pre-apply `rx=128` rounded rectangle clipping before embedding frames in the `.ico` file. All frames use a transparent background outside the rounded shape.

> **Note:** Pillow's native ICO export produces incorrect results. Multi-size `.ico` files must be built manually via struct packing.

| Embedded size | Usage |
|---|---|
| 16 × 16 | Taskbar small, Explorer list view |
| 32 × 32 | Explorer default, dialog icons |
| 48 × 48 | Explorer medium view |
| 64 × 64 | Explorer large view |
| 128 × 128 | Explorer extra-large |
| 256 × 256 | Explorer jumbo / high-DPI |

### 6.3 Linux

Use the same `rx=128` rounded rectangle PNG files as Windows. Provide PNGs at 16, 32, 48, 64, 128, and 256 px. Some desktop environments (GNOME 42+) may apply their own mask on top — this is acceptable.

---

## 7. Theme Compatibility

The icon uses a **single set of artwork for both dark and light system themes**. The gradient background (`#38BDF8` → `#006CBF`) provides sufficient contrast against both dark and light taskbars/Docks:

- **Dark taskbar:** the bright sky-blue top and white rocket body create strong separation.
- **Light taskbar:** the deeper `#006CBF` bottom and navy fins ensure the icon remains readable.

No separate dark/light variants are required. This matches the approach used across the Carina Studio suite.

---

## 8. Applying This Concept to Other Suite Apps

To adapt this rocket icon concept for another application in the Carina Studio suite:

1. Replace the background gradient colours with the target app's accent colour ramp.
2. Keep the rocket illustration, flame, and all proportions identical.
3. Update the nose cone colour to a mid-tone derived from the new accent.
4. Update the fins and window ring to a dark tone derived from the new accent.
5. Keep the white body and orange/amber/cream flame unchanged — these are suite-wide constants that provide cross-app visual coherence.
