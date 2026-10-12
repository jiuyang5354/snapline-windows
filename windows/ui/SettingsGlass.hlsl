sampler2D backdrop : register(s0);
sampler2D optics : register(s1);
float2 origin : register(c0);
float2 extent : register(c1);
float2 deflection : register(c2);
float veil : register(c3);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 map = tex2D(optics, uv);
    float2 sampleUv = origin + uv * extent;
    float2 shift = (map.rg - 128.0 / 255.0) * (255.0 / 127.0) * deflection;
    float3 color;
    color.r = tex2D(backdrop, sampleUv + shift * 1.04).r;
    color.g = tex2D(backdrop, sampleUv + shift).g;
    color.b = tex2D(backdrop, sampleUv + shift * .96).b;
    color = lerp(color, float3(.969, .980, .969), veil);
    color = lerp(color, float3(1, 1, 1), map.b * .62);
    return float4(color * map.a, map.a);
}
