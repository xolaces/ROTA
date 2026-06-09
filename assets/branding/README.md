# ROTA — Brand mark

The ROTA mark: an **R with a dragon tail wrapping off the back of the stem**, in pastel
orange-gold. Kept deliberately simple so it reads at favicon / app-tile sizes.

## Files
| File | Use |
|---|---|
| `rota-mark.svg` | Master glyph, transparent, gold gradient. The canonical mark — scale this anywhere. |
| `rota-mark-flat.svg` | Single flat colour (`#ECA962`) — for one-colour contexts (stamps, embroidery, small favicons). |
| `rota-icon-dark.svg` | App tile — mark on warm-slate `#241B16` with a soft glow. Primary launcher/store icon. |
| `rota-icon-light.svg` | App tile — mark on cream `#FBF4E6`. Light-theme alternate. |
| `rota-*-1024.png` | 1024×1024 raster exports of the above (mark PNG has transparent background). |

## Palette
| Token | Hex | Where |
|---|---|---|
| Gold (light stop) | `#F4CD86` | gradient top |
| Gold (mid stop) | `#EFB16A` | gradient middle |
| Amber (dark stop) | `#E89B55` | gradient bottom |
| Flat gold | `#ECA962` | one-colour mark |
| Tile dark | `#241B16` | dark app-tile background |
| Tile light | `#FBF4E6` | light app-tile background |

Corner radius on tiles: `rx=116` on a 512 grid (~22.7%).

## Re-exporting PNGs
SVG is the source of truth. To re-rasterise (Windows, headless Edge — note Edge cannot write
directly into a OneDrive-synced folder, so render to a temp path then copy):

```bash
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
"$EDGE" --headless=new --disable-gpu --force-device-scale-factor=2 \
  --screenshot="C:\\Users\\<you>\\AppData\\Local\\Temp\\out.png" --window-size=512,512 \
  "file:///<abs-path>/rota-icon-dark.svg"      # 1024px, opaque
# add --default-background-color=00000000 for a transparent export (the mark)
```
