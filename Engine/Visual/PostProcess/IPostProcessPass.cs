namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    public interface IPostProcessPass
    {
        // dipanggil sekali saat init (kalau perlu)
        void Init();

        // jalankan efek ini
        void Execute(uint inputTexture, int width, int height, float time);
    }

}
