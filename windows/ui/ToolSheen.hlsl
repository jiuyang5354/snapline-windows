sampler2D input : register(s0);
float2 center : register(c0);
float4 main(float2 uv : TEXCOORD) : COLOR {
    float4 color = tex2D(input, uv);
    float light = saturate(1.0 - length((uv - center) * float2(1.2, 2.3)));
    color.rgb += light * light * 0.075 * color.a;
    return color;
}
