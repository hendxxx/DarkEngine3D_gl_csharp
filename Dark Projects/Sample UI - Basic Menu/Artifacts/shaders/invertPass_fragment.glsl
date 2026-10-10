#version 330 core

in vec2 texCoords;
out vec4 FragColor;

uniform sampler2D sceneTex;
    
const float offset = 1.0 / 300.0;  

void main()
{
    // Invert the colors of the scene texture, do not change this line, it is used in the post processing effect
    vec2 texCoords = vec2(texCoords.x, 1.0 - texCoords.y); 
     
    //add here
    FragColor = vec4(vec3(1.0 - texture(sceneTex, texCoords)), 1.0);
 
 
}
