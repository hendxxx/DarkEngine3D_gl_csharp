## Sesi 2026-10-12 (lanjut 2): FPS masih 22 — audit di luar collision

**Penyebab besar baru yang ditemukan: biaya di Effect2DSystem weather path, BUKAN collision.**

### 1. Bug compile akibat edit sebelumnya (diperbaiki)
Edit cache `_groundCache` sebelumnya mengehapus blok kanan dan meninggalkan
**orphaned duplicate body dari GroundYAtAny** melayang di level class setelah
`ClearGroundCache()` → 94 error CS. Diperbaiki: hapus blok yatim, build kembali
succeeded 0 warn/err.

### 2. Audit log per-frame — BERSIH
Semua `Console.WriteLine` di Player2D/Dialogue/Trigger/Inventory/Quest/Shop/
Projectile/Effect2D event-driven (die-lebih satu kali per aksi), bukan per frame.
Telemetri [P2D-Tel] 1 Hz biaya minimal. Bukan penyebab 22 FPS.

### 3. **SpriteHitAt per-partikel × semua objek = biaya utama weather**
Per frame, tiap partikel jatuh memanggil SpriteHitAt 2× (body + surface), dan
setiap panggilan mengiterasi SEMUA scene object menjalankan
`TryGetPlayer2DDrawData` / `TryGetSprite2DDrawData` **quad-math + clip lookup
per objek**. Rain life 30 s = ribuan partikel → ratusan ribu quad-math/frame
pada scene peduli jumlah objek. Perbaikan:
- **Broad-phase per frame**: `BuildSpriteRects(mgr)` di awal `IntegrateParticles`
  menynapshot tiap sprite visible (rect X0/X1/Y0/Y1 + uv window + sheet path +
  availability pixel profile) ke buffer reusable, nol alloc steady-state.
- `SpriteHitAt` per partikel sekarang hanya cek X-range terhadap snapshot —
  per-pixel mahal hanya untuk sprite yang X-nya memuat tetes. Biaya per partikel
  turun dari O(objects × quad-math) ke O(sprite rects), dan per-pixel hanya
  untuk tetes yang benar-benar melintasi X-range sprite.
- Semantik identik: rect snapshot berasal dari draw-call yang sama dengan
  renderer, jadi hasil hit/rest/splash yang sama.

### 4. Ground cache dari sesi sebelumnya
`_groundCache` per (map, column) works correctly — partikel share surface Y
per kolom, tidak scan ulang tiap frame sampai di-invalidate.

Build Release: succeeded 0 warn/err. BrushLogicTest: 36 PASS / 0 FAIL / exit 0.

### Langkah validasi FPS user
1. Buka IDE Settings → GamePlay → aktifkan telemetri `[P2D-Tel]`.
2. Mainkan sampai rain aktif; catat `fps~` di window 1 detik.
3. **Sebelum perbaikan ini**: weather path bikin fps ~22; **sesudah**: biaya
   weather per-partikel turun drastis, FPS diharap naik signifikan.
4. Kalau masih rendah — cek apakah scene AIM punya banyak Sprite2D di kamera
   (per-pixel profile hanya dijalankan untuk tetes yang X-nya overlap sprite).

-Kompatibilitas: snapshot sprite rects setelah BuildSurfaceMaps + sebelum loop
integrasi partikel — overdraw untuk sprite yang pindah posisi frame ini masih
akurat (data diambil SETELAH update Manager via TryGet...DrawData live call).
