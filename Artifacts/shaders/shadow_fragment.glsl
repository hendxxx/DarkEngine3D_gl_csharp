#version 330 core

// EVSM warp constant (must match CSM.EVSM_Warp = 30.0)
uniform float evsmWarp;

out vec4 FragColor;

void main()
{
    float depth = gl_FragCoord.z;
    float c = evsmWarp;
    
    // Positive EVSM: exp(c*z), exp(2c*z)
    float pos = exp(c * depth);
    float pos2 = pos * pos; // exp(2c*z)
    
    // Negative EVSM: exp(-c*z), exp(-2c*z) 
    // (helps handle near occluders, reduces light bleeding)
    float neg = exp(-c * depth);
    float neg2 = neg * neg; // exp(-2c*z)
    
    // Store: R=pos, G=pos2, B=neg, A=neg2
    FragColor = vec4(pos, pos2, neg, neg2);
}
