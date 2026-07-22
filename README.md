# DarkEngine3D_gl_csharp

1. Pastikan glfw3.dll berada di folder yang sama dengan executable Anda.
2. Jika belum ada bisa didownload dari https://www.glfw.org/download.html
3. Pastikan folder Artifacts berada di folder yang sama dengan executable Anda.

How to run the apps:
1. WASD untuk mengerakan kamera
2. Mouse untuk melihat sekeliling
3. IJKL untuk mengerakan objek (segitiga)
4. Shift untuk meningkatkan kecepatan gerakan
5. Tekan P untuk melihat garis frustum dan kotak chunk
6. Kotak hijau adalah chunk yang sedang dirender, sedangkan kotak kuning adalah chunk yang tidak dirender karena berada di luar frustum kamera, pastikan tekan P.
6. Tekan ESC untuk keluar dari aplikasi.


jangan sentuh MainmenuScene.cs, LoadingScene.cs dan GameScene.cs, sekarang focus ke viewport
focus ke render background dulu, render ingame terkahir saja

so far issue sekarang dialog confirmation jadi overlay yg tinggal main2 visible aja

make it simple, hapus semua pilihan action, buat kan: 
1. open/closed overlay and choose which overlay
2. goto scene, and choose which scene.
3. exit, if in preview mode change to edit mode, if in game close the apps.



tambahkan action type, Close Current Overlay, jadi ketika overlay terbuka, dan user click no dengan action CLose Current Overlay, toggle overlay sesuai dengan parent buttonnya


1. perbaikai save filenya, actionya tidak tersimpan
2. untuk acion Close Overlay, tidak tertutup overlaynya