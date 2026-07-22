Intinya saya mau buat menu editor dengan manfaatkan scene, jadi scene ada 3 jenis:

1. MainMenu, defaultnya adalah blank, lalu user bisa add component dengan tehnik grid, tujuannya supaya bisa selalu rapih di posisi componentnya.
	di default scene bisa di add background image yang bisa di pilih autofill, zoom, atau filled. posisinya juga bisa diatur. tiap2 component bisa di atur ordernya.
	atau Main render (ingame) supaya bisa load glb model, terrain dll, jadi main menu bisa gambar atau active render.
	dimana user bisa add UI component seperti :
	- button, ada 2 action :
		1. toggle container visibility dimana yg di tampilkan atau action=exit confirmation, untuk membuat dialog confirmation box contoh untuk keluar, ada pilihan Yes atau NO.
		2. pindah scene, nanti tinggal pilih nextnya ke scene mana.
	- container, dipakai untuk group of UI, bisa juga untuk confirmation dialog.
	- slider, digunakan untuk atur value, seperti resolusi, FOV, quality of shadow dll.
	- checkbox, digunakan untuk pilih yes dan no, contoh render shadow? yes / no
	- dropdown, sama seperti slider tapi bentuk nya dropdown box
	- textbox, isian bisa digunakan untuk save.
	
	default:
	- Exit Confirmation, buatkan jika user pilih exit confirmation.
	- Loading box, buat X slot lengkap dengan gambar di kiri dan indo file yang disave di sebelahnya.
	- Save Confirmation, bisa digunakan untuk save game.
	- Load Confirmationm bisa digunakan untuk load game.

2. Loading Screen, bisa di add gambar dan posisi text loading info dan 1 image rotation.
	
3. Ingame, Main Render semua fungsi game engine ada disini, action sementara:
	- add terrain, bisa di atur ukuran, heightmap, texture 1-3 dll sesuai dengan kebutuhan
	- add glb object, bisa atur LOD dll
	- add skyboxm bisa atur awan, matahari dan bulan
	- add player
	- add AI
	- add HUD