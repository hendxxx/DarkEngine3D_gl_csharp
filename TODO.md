# DarkEngine3D_gl_csharp
Pending:
1. buatkan shadow untuk object, jadi ketika object berada di atas permukaan, akan ada bayangan yang mengikuti gerakan object tersebut.
2. buatkan shadow dengan tehnik CSM, di dalam sudah ada LOD untuk Terrain bisa di manfaatkan untuk membuat shadow dengan kualitas yang baik tanpa harus menggunakan banyak resource.
3. pastikan shadow bisa di render dengan baik di semua jenis terrain, jadi ketika object berada di atas permukaan yang berbeda, shadow tetap bisa terlihat dengan baik. 

Done:
1. object sudah di load menggunakan file glb, dan sudah bisa di render menggunakan opengl, namun belum bisa di animasi. sekarang objectnya numpuk di tengah dan masih diam.
2. nantinya ada file animasi yamg isinya animasi saja tanpa object yang bisa di load ke engine, dan bisa di apply ke object yang sudah di load sebelumnya. jadi nanti ada 2 file, satu untuk model dan satu untuk animasi. jadi bisa ganti animasi tanpa harus load ulang modelnya.
3. default objecnya adalah animasi idle atau animasi pertama yang ada di file animasi, jadi ketika modelnya sudah di load, langsung tampil dengan animasi idle atau animasi pertama yang ada di file animasi.
4. buat semua gerakan animasi idle dilooping, jadi ketika animasi idle sudah selesai, langsung mulai lagi dari awal tanpa jeda.
5. jika ada garakan lain misal animasi berjalan, animasi berjalan akan menggantikan animasi idle, jadi ketika animasi berjalan sudah selesai, langsung kembali ke animasi idle, dengan menggunakan tehnik animasi blending, jadi ketika animasi berjalan sudah selesai, langsung kembali ke animasi idle dengan transisi yang halus tanpa jeda.
6. buat animasi berjalan dilooping, ketika object bergerak.
7. nantinya akan ada beberapa state animasi yang bisa di trigger oleh action tertentu.