Scene Editor System
Overview
Editor menggunakan konsep Scene-Based Editor.
Terdapat 3 jenis scene utama:
1.	Main Menu Scene
2.	Loading Screen Scene
3.	Ingame Scene
Setiap project dapat memiliki banyak scene dan scene dapat saling terhubung melalui action seperti Change Scene.
________________________________________
1. Main Menu Scene
Deskripsi
Digunakan untuk membuat tampilan menu game, settings, save/load menu, dan UI lainnya.
Main Menu memiliki 2 mode render:
UI Mode
Menggunakan background image dan UI Component.
Render Mode
Menggunakan renderer engine secara langsung sehingga Main Menu dapat menampilkan:
•	GLB Model
•	Terrain
•	Skybox
•	Lighting
•	Animasi Scene
Contoh:
•	Character Selection
•	Main Menu 3D
•	Garage Menu
•	Lobby Scene
________________________________________
Background
Mendukung:
Background Image
Mode:
•	Auto Fill
•	Zoom
•	Stretch / Filled
Properties:
•	Position X/Y
•	Scale
•	Rotation
•	Layer Order
Main Render
Menggunakan scene render real-time sebagai background.
Support:
•	GLB Model
•	Terrain
•	Skybox
•	Lighting
________________________________________
UI Layout System
Menggunakan Grid Based Layout.
Tujuan:
•	Posisi komponen tetap rapi
•	Mudah responsive
•	Snap ke grid
Fitur:
•	Grid Size
•	Snap Enable/Disable
•	Manual Position Override
•	Component Ordering (Z-Order)
________________________________________
UI Components
Button
Action Type:
Toggle Container Visibility
Digunakan untuk:
•	Show Panel
•	Hide Panel
•	Open Dialog
•	Exit Confirmation
Change Scene
Digunakan untuk berpindah scene.
Property:
•	Target Scene
________________________________________
Container
Digunakan sebagai group UI.
Contoh:
•	Settings Window
•	Confirmation Dialog
•	Save Slot Window
Property:
•	Visibility
•	Layer Order
•	Child Components
________________________________________
Slider
Digunakan untuk pengaturan numerik.
Contoh:
•	FOV
•	Volume
•	Render Distance
•	Shadow Quality
•	Resolution Scale
Property:
•	Min Value
•	Max Value
•	Step
•	Current Value
________________________________________
Checkbox
Digunakan untuk boolean setting.
Contoh:
•	Enable Shadow
•	Fullscreen
•	VSync
•	Show HUD
Property:
•	Checked
•	Unchecked
________________________________________
Dropdown
Digunakan untuk memilih satu nilai dari daftar.
Contoh:
•	Resolution
•	Language
•	Shadow Quality
•	Texture Quality
Property:
•	Options
•	Selected Value
________________________________________
TextBox
Input data dari user.
Contoh:
•	Save Name
•	Player Name
Property:
•	Placeholder
•	Max Length
•	Current Value
________________________________________
Built-In Templates (Default)
Exit Confirmation
Template dialog siap pakai.
Component:
•	Title
•	Message
•	Yes Button
•	No Button
Action:
•	Yes → Exit Game
•	No → Close Dialog
________________________________________
Loading Box
Template daftar save game.
Property:
•	Dynamic Slot Count
Per Slot:
•	Thumbnail Image
•	Save Name
•	Save Date
•	Additional Info
________________________________________
Save Confirmation
Dialog konfirmasi save.
Component:
•	Message
•	Confirm Button
•	Cancel Button
________________________________________
Load Confirmation
Dialog konfirmasi load.
Component:
•	Message
•	Confirm Button
•	Cancel Button
________________________________________
2. Loading Screen Scene
Deskripsi
Digunakan ketika perpindahan scene atau loading asset.
________________________________________
Components
Background Image
Support:
•	Stretch
•	Fill
•	Zoom
________________________________________
Loading Information Text
Menampilkan:
•	Current Task
•	Progress Status
•	Tips
Property:
•	Position
•	Font
•	Alignment
________________________________________
Rotating Image
Digunakan sebagai loading indicator.
Property:
•	Rotation Speed
•	Direction
•	Image Asset
________________________________________
3. Ingame Scene
Deskripsi
Scene utama permainan.
Seluruh fitur engine berjalan di scene ini.
________________________________________
Terrain System
Support:
•	Terrain Size
•	Heightmap
•	Texture Layer 1
•	Texture Layer 2
•	Texture Layer 3
Property:
•	Width
•	Length
•	Height Scale
________________________________________
GLB Object
Support:
•	Import GLB
•	Static Object
•	Dynamic Object
Property:
•	Position
•	Rotation
•	Scale
•	LOD Configuration
________________________________________
Skybox System
Support:
•	Skybox Texture
•	Cloud Control
•	Sun Control
•	Moon Control
Property:
•	Time Of Day
•	Cloud Density
•	Sun Intensity
•	Moon Intensity
________________________________________
Player System
Add Player Entity.
Property:
•	Spawn Position
•	Movement Controller
•	Camera Controller
________________________________________
AI System
Add AI Entity.
Property:
•	Spawn Position
•	Behavior Type
•	Patrol Path
________________________________________
HUD System
UI khusus gameplay.
Contoh:
•	Health Bar
•	Stamina Bar
•	Ammo Counter
•	Mini Map
•	Quest Tracker
•	Crosshair

