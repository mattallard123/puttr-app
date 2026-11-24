// Shared by the Tracks ground, tree and sky shaders: the sky's picture, the haze toward the horizon and the light
// the sky gives. All set as globals by TracksReal (the Tracks world is the only thing that draws with these).
#ifndef PUTTR_TRACKS_COMMON
#define PUTTR_TRACKS_COMMON

TEXTURE2D(_TracksSkyTex); SAMPLER(sampler_TracksSkyTex);
float4 _TracksHaze;            // rgb: the horizon's colour (linear); a: how fast the air hazes over, per yard
float4 _TracksAmbientSky;      // the light from overhead
float4 _TracksAmbientGround;   // the light off the ground
float _TracksSkyTurn;          // turns the panorama so its sun stands where the scene's sun is

// The sky in a direction: the top half of a panorama (u round the horizon, v up to the zenith), hazed at the horizon.
half3 TracksSky(float3 dir)
{
    float u = atan2(dir.x, dir.z) * 0.15915494 + _TracksSkyTurn;
    float v = asin(saturate(dir.y)) * 0.63661977;
    half3 c = SAMPLE_TEXTURE2D_LOD(_TracksSkyTex, sampler_TracksSkyTex, float2(u, min(v, 0.998)), 0).rgb;
    return lerp(_TracksHaze.rgb, c, smoothstep(0.0, 0.06, dir.y));
}

half3 TracksAmbient(half3 n) { return lerp(_TracksAmbientGround.rgb, _TracksAmbientSky.rgb, n.y * 0.5 + 0.5); }

// Distance haze: unit is how many world units make a yard (the putting set is built in inches). The haze lies in a
// layer over the ground, thinning with height (to a third every 50 yd), so a camera high above (the plan view, the
// blimp) looks down through very little of it, however far off to the side the ground is.
half3 TracksHazed(half3 c, float3 positionWS, float unit)
{
    float3 away = (positionWS - _WorldSpaceCameraPos) / unit;
    float rise = abs(away.y) / 50.0;
    float d = length(away) * (1.0 - exp(-rise)) / max(rise, 1e-3);
    return lerp(c, _TracksHaze.rgb, 1.0 - exp(-d * _TracksHaze.a));
}

#endif
