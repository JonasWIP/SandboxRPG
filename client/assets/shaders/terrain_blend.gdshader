shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform vec4 sand_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec4 grass_color = vec4(0.0, 1.0, 0.0, 1.0);
uniform float blend_height : hint_range(0.0, 4.0) = 0.5;
uniform float blend_sharpness : hint_range(0.1, 10.0) = 3.0;

varying float world_y;

void vertex() {
    world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}

void fragment() {
    float t = clamp((world_y - blend_height) * blend_sharpness, 0.0, 1.0);
    ALBEDO = mix(sand_color.rgb, grass_color.rgb, t);
    ROUGHNESS = 0.9;
    METALLIC = 0.0;
}
