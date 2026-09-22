FEATURE REQUEST - 2D SIDE SCROLLING ENGINE IMPROVEMENT


Saya ingin melakukan improvement pada engine 2D side-scrolling agar lebih mudah digunakan dan menghasilkan gameplay yang lebih nyaman. Berikut detail kebutuhan yang harus diimplementasikan.


====================================================
1. CAMERA SYSTEM REVAMP
====================================================


Tujuan:
Membuat sistem kamera seperti game platformer modern (Mario, Ori, Hollow Knight, Dead Cells, dll) yang smooth, memiliki batas map, dan mudah dikonfigurasi dari editor.


1.1 Camera Start Position


Tambahkan component baru:


- Camera Start Point


User dapat meletakkannya di scene.


Ketika game dijalankan:


- Kamera langsung menggunakan posisi Camera Start Point.
- Posisi Camera Start Point menjadi center kamera saat start.
- Tambahkan parameter Start Zoom untuk menentukan zoom awal kamera.
- Zoom awal harus bisa diubah melalui Inspector.
- Saat ini kamera terasa terlalu jauh sehingga zoom awal perlu bisa dikustomisasi.


Contoh:
Start Zoom = 2.0


1.2 Camera World Boundary


Sistem harus otomatis menghitung batas dunia berdasarkan tile yang ada di map.


Hitung:


- World Left
- World Right
- World Top
- World Bottom


berdasarkan tile paling kiri, kanan, atas, dan bawah yang ditemukan di seluruh tilemap.


Rule:


- Kamera tidak boleh menampilkan area di luar map.
- Kamera tidak boleh bergerak melewati World Left.
- Kamera tidak boleh bergerak melewati World Right.
- Kamera tidak boleh bergerak melewati World Top.
- Kamera tidak boleh bergerak melewati World Bottom.


Dengan kata lain, viewport kamera harus selalu berada di dalam area map yang valid.


1.3 Player Follow


Default behaviour:


- Kamera berusaha menjaga player berada di tengah layar.
- Namun kamera tidak wajib selalu tepat di tengah.
- Gunakan smooth follow agar pergerakan kamera tidak terasa kaku.


Tambahkan parameter:


- Follow Speed


di Inspector.


1.4 Vertical Camera Rule


Masalah saat ini:


- Kamera ikut naik terus setiap player melompat.
- Gameplay menjadi tidak nyaman.


Rule baru:


- Kamera boleh naik hanya ketika player bergerak cukup tinggi melewati threshold tertentu.
- Tambahkan parameter Vertical Threshold.


Contoh:


Vertical Threshold = 64 px


Jika player melompat kurang dari threshold:


- Kamera tidak bergerak naik.


Jika player melewati threshold:


- Kamera boleh mengikuti ke atas.


Saat player turun kembali:


- Kamera harus perlahan kembali ke posisi player.
- Tambahkan parameter Return To Player Speed.


1.5 Dead Zone


Tambahkan fitur Dead Zone.


Parameter:


- Dead Zone Width
- Dead Zone Height


Rule:


- Selama player masih berada di dalam area dead zone, kamera tidak bergerak.
- Kamera mulai bergerak hanya ketika player keluar dari area tersebut.


Tujuan:


- Mengurangi gerakan kamera yang terlalu sering.
- Gameplay terasa lebih smooth.


1.6 Look Ahead


Tambahkan fitur Look Ahead.


Parameter:


- Look Ahead Distance


Rule:


Jika player bergerak ke kanan:


- Kamera sedikit melihat ke depan (kanan).


Jika player bergerak ke kiri:


- Kamera sedikit melihat ke depan (kiri).


Contoh:


Look Ahead Distance = 150 px


Tujuan:


- Player dapat melihat obstacle lebih awal.


====================================================
2. PLAYER MOVEMENT CONFIGURATION
====================================================


Tujuan:


Saat ini player bergerak terlalu lambat dan parameter masih sulit dikonfigurasi.


Semua parameter movement harus tersedia di Inspector.


Tambahkan:


- Move Speed
- Run Speed
- Jump Force
- Gravity Scale
- Acceleration
- Deceleration
- Air Control


Contoh:


Move Speed = 300
Run Speed = 500


Perubahan inspector harus langsung digunakan saat game dijalankan tanpa perlu mengubah source code.


====================================================
3. ANIMATION ACTION SYSTEM
====================================================


Tujuan:


Saat ini animasi masih hardcoded.


Saya ingin sistem action animation yang bisa ditambah oleh user tanpa coding.


3.1 Animation Action Panel


Buat panel baru:


Animation Actions


Default action:


- Idle
- Walk
- Run
- Jump
- Fall
- Attack
- Block
- Dead
- Spawn


3.2 Add Action


Tambahkan tombol:


[ + ]


untuk membuat action baru.


Contoh:


- Dash
- Fireball
- Skill A
- Skill B
- Ultimate
- Custom Action


User bebas menambahkan sebanyak yang dibutuhkan.


3.3 Animation Assignment


Setiap action memiliki:


- Action Name
- Animation Clip
- Loop
- Priority


Contoh:


Attack
 -> Attack01.anim


3.4 Keyboard Binding


Setiap action bisa dikaitkan dengan keyboard.


Contoh:


Attack = J
Block = K
Dash = L
Skill A = U
Skill B = I


Semua pengaturan dilakukan melalui Inspector.


3.5 Animation Priority


Tambahkan sistem prioritas.


Contoh:


Dead Priority = 100


Jika Dead aktif:


- Semua action lain dibatalkan.


Contoh:


Walk Priority = 5


Dapat diinterupsi oleh Attack atau Skill.


====================================================
4. EFFECT SYSTEM
====================================================


Tujuan:


Action tidak hanya memutar animasi tetapi juga dapat menghasilkan effect.


4.1 Action Events


Setiap action memiliki:


- On Start
- On Frame
- On End


4.2 Spawn Effect


Contoh:


Attack


Saat action dijalankan dapat memunculkan:


- Fire Effect
- Spark Effect
- Explosion Effect
- Smoke Effect


4.3 Effect Properties


Setiap effect memiliki:


- Direction
- Speed
- Lifetime
- Scale
- Rotation


4.4 Direction


Pilihan:


- Forward
- Backward
- Up
- Down
- Custom Direction


4.5 End Effect


Pilihan:


- Destroy
- Fade Out
- Scale Down
- Explode
- Loop


Contoh Fireball:


Direction = Forward
Speed = 600
Lifetime = 3 Detik
End Effect = Fade Out


====================================================
5. SPRITE LIBRARY / ANIMATION LIBRARY
====================================================


Tujuan:


Mempermudah penggunaan ulang asset animasi.


Buat panel baru:


- Sprite Library
atau
- Animation Library


5.1 Asset Browser


Semua asset ditampilkan dalam bentuk thumbnail visual.


Contoh:


[Camp Fire]
[Explosion]
[Smoke]
[Torch]
[Fireball]
[Coin Spin]


Bukan hanya nama file.


5.2 Drag And Drop


User bisa:


- Drag animation
- Drop ke action slot
- Drop ke effect slot
- Drop langsung ke scene


5.3 Reusable Assets


Jika user sudah membuat asset Camp Fire:


- Tidak perlu membuat ulang.
- Cukup drag dari library ke scene atau action yang diinginkan.


====================================================
6. SPRITE PREFAB SYSTEM
====================================================


Tujuan:


Membuat object baru dari sprite dengan cepat tanpa membuat entity dari nol.


Tambahkan menu:


Add Sprite


mirip Add Player tetapi lebih sederhana.


Saat membuat Add Sprite, sistem otomatis menambahkan:


- Transform
- Sprite Renderer
- Animator


Opsional:


- Collider


Jangan otomatis menambahkan:


- Player Controller
- Camera Follow
- Input Controller


Use Case:


Untuk membuat:


- Api unggun
- Obor
- Coin
- Explosion
- Chest
- Decoration
- Environment Objects


Workflow yang diharapkan:


Add Sprite
→ Drag Animation dari Sprite Library
→ Letakkan di Scene
→ Selesai


====================================================
PRIORITAS IMPLEMENTASI
====================================================


HIGH PRIORITY


- Camera Start Point
- Camera Boundary
- Camera Zoom
- Vertical Camera Rule
- Player Speed Inspector
- Animation Action System


MEDIUM PRIORITY


- Effect System
- Sprite Library
- Drag & Drop Animation
- Keyboard Binding


LOW PRIORITY


- Animation Priority Advanced Rules
- Timeline Event System
- Advanced Effect Transition


Expected Result:


Editor dapat digunakan seperti mini Unity 2D editor, dimana designer bisa membuat character, animation, action, effect, dan asset reusable tanpa perlu mengubah source code atau melakukan hardcode animation setiap kali menambah konten baru.


====================================================
7. TRIGGER ZONE / EVENT AREA SYSTEM
====================================================


Tujuan:


Saat ini Collision Tile hanya digunakan untuk menghalangi player.


Saya membutuhkan jenis collision baru yang berfungsi sebagai Trigger Area.


Trigger Area dapat ditembus oleh player dan object lain, namun dapat menjalankan event tertentu saat terjadi interaksi.


7.1 Trigger Area Component


Tambahkan component baru:


- Trigger Area


Visual editor sebaiknya sama seperti Collision Tile saat ini.


Perbedaannya:


- Player dapat menembus area tersebut.
- Tidak memiliki collision fisik.
- Hanya digunakan untuk mendeteksi event.


7.2 Trigger Events


Saat player masuk ke area trigger, sistem dapat menjalankan berbagai aksi.


Contoh:


- Save Game
- Load Checkpoint
- Pindah Map
- Play Sound
- Play Music
- Spawn Effect
- Spawn Object
- Start Dialogue
- Start Cutscene
- Camera Shake
- Unlock Door
- Give Item
- Activate Quest
- Complete Quest
- Jalankan Script


7.3 Trigger Conditions


Tambahkan opsi:


- On Enter
- On Stay
- On Exit


Contoh:


On Enter:
Player masuk area save point.


On Stay:
Player berada di area healing.


On Exit:
Player keluar area tertentu lalu event dijalankan.


7.4 Visual Editor


Trigger Area harus bisa diedit langsung di scene editor menggunakan kotak seperti collision tile saat ini.


User dapat:


- Resize
- Drag
- Copy
- Paste
- Duplicate


====================================================
8. EVENT CREATOR SYSTEM
====================================================


Tujuan:


Menghilangkan kebutuhan hardcode event di source code.


Game designer harus dapat membuat logic gameplay dari editor.


8.1 Script Event (Versi Awal)


Untuk tahap awal, event dapat dibuat menggunakan script editor.


Contoh:


Trigger Area
→ Execute Script


Script bisa mengakses:


- Player
- Enemy
- Camera
- Sound
- Effect
- Map
- UI


Contoh penggunaan:


- Buka pintu
- Spawn musuh
- Memulai dialog
- Memainkan suara


8.2 Node Event Editor (Versi Lanjutan)


Jika memungkinkan, buat visual scripting menggunakan node editor.


Mirip:


- Unreal Blueprint
- Godot Visual Script
- Unity Shader Graph Style


User dapat membuat flow event tanpa coding.


Contoh:


Player Enter Trigger
↓
Play Sound
↓
Spawn Effect
↓
Delay 2 Sec
↓
Open Door
↓
Play Cutscene


8.3 Event Nodes


Node dasar yang diperlukan:


Event


- On Trigger Enter
- On Trigger Exit
- On Action
- On Button Press


Flow


- Delay
- Branch
- If
- Compare Value
- Loop


Gameplay


- Spawn Object
- Destroy Object
- Enable Object
- Disable Object


Audio


- Play Sound
- Stop Sound


UI


- Show Text
- Show Dialogue
- Hide UI


Camera


- Camera Move
- Camera Shake
- Camera Zoom


Scene


- Load Scene
- Change Map


====================================================
9. GAMEPAD INPUT SYSTEM
====================================================


Tujuan:


Game harus dapat dimainkan menggunakan controller/gamepad selain keyboard.


9.1 Supported Devices


Minimal support:


- Xbox Controller
- Xbox One Controller
- Xbox Series Controller
- Generic USB Controller
- DualShock 4
- DualSense PS5


9.2 Input Mapping


Buat Input Manager baru.


User dapat mengatur:


Move Left
Move Right
Move Up
Move Down
Jump
Attack
Block
Dash
Skill A
Skill B
Pause


9.3 Button Remap


Semua tombol dapat diubah melalui editor.


Contoh:


Attack
→ Keyboard J
→ Gamepad X


Jump
→ Keyboard Space
→ Gamepad A


9.4 Analog Support


Support:


- Left Stick
- Right Stick
- Trigger LT
- Trigger RT


Gunakan analog value 0 - 1.


====================================================
10. AUDIO SYSTEM
====================================================


Tujuan:


Menambahkan dukungan sound effect dan background music yang fleksibel.


10.1 Audio Categories


Pisahkan audio menjadi:


- Master
- Music
- SFX
- Voice
- Ambient


10.2 Sound Component


Tambahkan Audio Source Component.


Property:


- Audio Clip
- Volume
- Pitch
- Loop
- Play On Start


10.3 Background Music


Buat Music Manager.


Fungsi:


- Play Music
- Stop Music
- Pause Music
- Resume Music
- Change Music
- Fade In
- Fade Out


Contoh:


Masuk area boss:


Normal Music
↓
Fade Out
↓
Boss Music
↓
Fade In


10.4 Sound Triggers


Sound dapat dipanggil dari:


- Trigger Area
- Animation Event
- Event Creator
- Dialogue
- Cutscene


====================================================
11. CUTSCENE SYSTEM
====================================================


Tujuan:


Memungkinkan pembuatan story sequence seperti game RPG dan platformer modern.


11.1 Cutscene Asset


Tambahkan asset baru:


Cutscene


Cutscene dapat disimpan dan digunakan berulang kali.


11.2 Timeline System


Cutscene menggunakan timeline.


Berisi:


- Camera Track
- Character Track
- Animation Track
- Dialogue Track
- Audio Track
- Effect Track


11.3 Camera Control


Selama cutscene:


- Camera dapat dipindah
- Camera dapat zoom
- Camera dapat fade
- Camera dapat shake


11.4 Character Control


Character dapat:


- Move To Position
- Play Animation
- Face Left
- Face Right
- Hide
- Show


11.5 Cutscene Events


Contoh:


Player Enter Trigger
↓
Start Cutscene
↓
Camera Move
↓
NPC Walk
↓
Dialogue
↓
Play Music
↓
End Cutscene
↓
Return Control To Player


11.6 Cutscene Skip


Tambahkan opsi:


- Allow Skip


Default button:


ESC
START


====================================================
12. DIALOGUE / BUBBLE WORD SYSTEM
====================================================


Tujuan:


Player dapat berbicara dengan NPC seperti game RPG.


12.1 Dialogue Bubble


Tambahkan Bubble Dialogue.


Bubble muncul di atas character yang sedang berbicara.


Contoh:


[NPC]
"Selamat datang di desa kami."


Bubble mengikuti posisi karakter.


12.2 Speaker System


Setiap dialogue harus mengetahui siapa yang berbicara.


Contoh:


Speaker:
- Player
- NPC
- Monster
- Narrator


12.3 RPG Conversation Mode


Tambahkan mode dialog RPG.


Tampilan:


Portrait Character


NPC:
"Apakah kamu ingin masuk ke dungeon?"


[ Ya ]
[ Tidak ]


12.4 Dialogue Features


Support:


- Next Page
- Auto Play
- Typewriter Effect
- Skip
- Fast Forward


12.5 Dialogue Events


Dialogue dapat menjalankan event.


Contoh:


NPC:
"Aku akan membuka gerbang."


↓


Play Animation Door Open


↓


Play Sound


↓


Unlock Area


12.6 Choice System


Support pilihan dialog.


Contoh:


NPC:
"Mau menerima quest?"


Pilihan:


- Ya
- Tidak


Jika Ya:
Start Quest


Jika Tidak:
Close Dialogue


12.7 Rich Text Support


Support:


- Warna teks
- Bold
- Italic
- Nama karakter
- Icon item
- Icon quest


12.8 Localization Ready


Struktur dialog harus dipersiapkan agar di masa depan bisa mendukung:


- Indonesia
- English
- Japanese
- Chinese


tanpa perlu membuat ulang sistem dialog.


====================================================
HIGH PRIORITY
====================================================


- Trigger Area System
- Event Creator (Script Version)
- Audio System
- Gamepad Support
- Dialogue Bubble


====================================================
MEDIUM PRIORITY
====================================================


- Node Event Editor
- RPG Dialogue System
- Music Manager
- Cutscene Timeline


====================================================
LOW PRIORITY
====================================================


- Advanced Visual Scripting
- Branching Dialogue
- Localization
- Cinematic Camera Features


Expected Result:


Engine dapat digunakan untuk membuat game platformer, metroidvania, action RPG, dan RPG dengan sistem inventory, equipment visual, status character, HUD, quick slot, quest tracking, dan navigation tanpa perlu melakukan hardcode pada source code.




Implement a Terrain system on top of the existing PBR-enabled Plane mesh.

Requirements:

1. Terrain Geometry
- Add support for generating terrain from a grayscale heightmap.
- Each pixel value controls vertex height.
- Terrain resolution must be configurable.
- Support terrain size, height scale, and height offset parameters.
- Generate normals automatically after displacement.

2. Compatibility with Existing PBR
- Preserve the existing PBR rendering pipeline.
- Terrain must continue supporting:
  - Albedo/Base Color
  - Normal Map
  - Metallic
  - Roughness
  - Ambient Occlusion
  - Emissive

3. Terrain Layers
- Add multi-layer terrain painting.
- Each layer should support a full PBR material set:
  - Albedo
  - Normal
  - Roughness
  - Metallic
  - AO
  - Height Map (optional)
- Minimum support for 4 layers.
- Layers should blend using splat maps.

4. Height-Based Material Blending
- Support height-aware blending between layers.
- Rock should naturally protrude through grass instead of simple linear blending.
- Blend weights should be normalized.

5. Vertex Displacement
- Heightmap terrain defines the macro shape.
- Material vertex displacement should remain optional.
- Final vertex position:
    Terrain Heightmap + Material Displacement
- Material displacement scale must be configurable.
- Prevent double displacement when the same height texture is used.

6. Terrain Editing
- Runtime or editor support for:
  - Import Heightmap
  - Export Heightmap
  - Paint Terrain Layers
  - Adjust Terrain Height Scale

7. Performance
- Implement terrain chunking.
- Support LOD per chunk.
- Avoid cracks between LOD levels.
- Optimize for large open worlds.

8. Rendering
- Support triplanar mapping as an option.
- Prevent texture stretching on steep slopes.
- Support slope-based automatic material assignment:
  - Grass on flat areas
  - Rock on steep areas
  - Snow on high elevations

Goal:
Create a modern terrain system comparable to Unity Terrain or Unreal Landscape while remaining fully compatible with the existing PBR material framework.

Rekomendasi arsitektur:
Terrain
 ├─ Heightmap (Geometry)
 ├─ Splatmap (Layer Weights)
 ├─ Layer 0 : Grass PBR
 ├─ Layer 1 : Rock PBR
 ├─ Layer 2 : Sand PBR
 ├─ Layer 3 : Snow PBR
 └─ Optional Micro Vertex Displacement