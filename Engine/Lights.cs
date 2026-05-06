using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DarkEngine3D_gl_csharp.Engine
{

    public unsafe class Lights
    { 

        uint shaderProgram;
        int sunDirLoc, viewPosLoc, lightColorLoc;
        Vector3 vsunDirLoc;
        Vector3 vviewPosLoc;
        Vector3 vlightColorLoc;


        public Lights( Vector3 _sundir, Vector3 _lightColor, Vector3 _viewpos) {


            vsunDirLoc = _sundir;
            vviewPosLoc = _viewpos;
            vlightColorLoc = _lightColor;
            Init();    
        }

        //Init light location
        public void Init()
        {
            shaderProgram = Shader.GetShaderProgram();

            sunDirLoc = GL.GetUniformLocation(shaderProgram, "sunDir");
            viewPosLoc = GL.GetUniformLocation(shaderProgram, "viewPos");
            lightColorLoc = GL.GetUniformLocation(shaderProgram, "lightColor"); 
        }

        public void Update() {
            GL.UseProgram(shaderProgram); 
            GL.Uniform3f(sunDirLoc, vsunDirLoc.X, vsunDirLoc.Y, vsunDirLoc.Z);
            GL.Uniform3f(lightColorLoc, vlightColorLoc.X, vlightColorLoc.Y, vlightColorLoc.Z);
            GL.Uniform3f(viewPosLoc, vviewPosLoc.X, vviewPosLoc.Y, vviewPosLoc.Z);


        }
    }
}
