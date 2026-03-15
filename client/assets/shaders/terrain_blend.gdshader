shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform sampler2D sand_texture : source_color, hint_default_white;
uniform sampler2D grass_texture : source_color, hint_default_white;
uniform float blend_height : hint_range(0.0, 4.0) = 0.5;
uniform float blend_sharpness : hint_range(0.1, 10.0) = 3.0;
uniform vec2 uv_scale = vec2(8.0, 8.0);

// Interpolated world-space Y passed from vertex shader
varying float world_y;

void vertex() {
    // MODEL_MATRIX transforms local VERTEX to world space
    world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}

void fragment() {
    vec2 uv = UV * uv_scale;
    vec4 sand  = texture(sand_texture,  uv);
    vec4 grass = texture(grass_texture, uv);

    // Blend based on world-space height — sand at beach (Y≈0), grass on plateau (Y≈4)
    float t = clamp((world_y - blend_height) * blend_sharpness, 0.0, 1.0);
    ALBEDO = mix(sand.rgb, grass.rgb, t);
    ROUGHNESS = 0.9;
    METALLIC  = 0.0;
}
