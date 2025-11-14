use wasm_bindgen::prelude::*;
use web_sys::{window, HtmlCanvasElement, WebGlRenderingContext as GL, WebGlProgram, WebGlShader};
use std::rc::Rc;
use std::cell::RefCell;

#[wasm_bindgen]
pub fn start(canvas_id: &str) -> Result<(), JsValue> {
    console_error_panic_hook::set_once();
    wasm_logger::init(wasm_logger::Config::default());

    let window = window().ok_or("no global window")?;
    let document = window.document().ok_or("no document")?;
    let canvas = document
        .get_element_by_id(canvas_id)
        .ok_or("canvas not found")?
        .dyn_into::<HtmlCanvasElement>()?;

    let gl: GL = canvas
        .get_context("webgl")?
        .ok_or("webgl not supported")?
        .dyn_into()?;

    // Vertex shader
    let vert_code = r#"
        attribute vec2 position;
        void main() {
            gl_Position = vec4(position, 0.0, 1.0);
        }
    "#;

    // Fragment shader
    let frag_code = r#"
        void main() {
            gl_FragColor = vec4(0.2, 0.7, 0.9, 1.0);
        }
    "#;

    let vert = compile_shader(&gl, GL::VERTEX_SHADER, vert_code)?;
    let frag = compile_shader(&gl, GL::FRAGMENT_SHADER, frag_code)?;
    let program = link_program(&gl, &vert, &frag)?;
    gl.use_program(Some(&program));

    // triangle data
    let vertices: [f32; 6] = [
        0.0,  0.8,
       -0.8, -0.8,
        0.8, -0.8,
    ];

    let buffer = gl.create_buffer().ok_or("failed to create buffer")?;
    gl.bind_buffer(GL::ARRAY_BUFFER, Some(&buffer));

    // upload data
    unsafe {
        let vert_array = js_sys::Float32Array::view(&vertices);
        gl.buffer_data_with_array_buffer_view(GL::ARRAY_BUFFER, &vert_array, GL::STATIC_DRAW);
    }

    let pos_attrib = gl.get_attrib_location(&program, "position") as u32;
    gl.enable_vertex_attrib_array(pos_attrib);
    gl.vertex_attrib_pointer_with_i32(pos_attrib, 2, GL::FLOAT, false, 0, 0);

    // Animation loop
    let f = Rc::new(RefCell::new(None));
    let g = f.clone();

    let gl_rc = Rc::new(gl);

    *g.borrow_mut() = Some(Closure::wrap(Box::new(move || {
        let gl = &*gl_rc;
        gl.clear_color(0.05, 0.05, 0.08, 1.0);
        gl.clear(GL::COLOR_BUFFER_BIT);
        gl.draw_arrays(GL::TRIANGLES, 0, 3);

        // schedule next frame
        let window = web_sys::window().unwrap();
        window
            .request_animation_frame(f.borrow().as_ref().unwrap().as_ref().unchecked_ref())
            .expect("should register requestAnimationFrame");
    }) as Box<dyn FnMut()>));

    // start loop
    let window = web_sys::window().unwrap();
    window
        .request_animation_frame(g.borrow().as_ref().unwrap().as_ref().unchecked_ref())
        .map_err(|e| format!("request_animation_frame failed: {:?}", e))?;

    Ok(())
}

fn compile_shader(gl: &GL, shader_type: u32, source: &str) -> Result<WebGlShader, JsValue> {
    let shader = gl.create_shader(shader_type).ok_or("unable to create shader")?;
    gl.shader_source(&shader, source);
    gl.compile_shader(&shader);
    if gl.get_shader_parameter(&shader, GL::COMPILE_STATUS).as_bool().unwrap_or(false) {
        Ok(shader)
    } else {
        Err(JsValue::from_str(&gl.get_shader_info_log(&shader).unwrap_or_default()))
    }
}

fn link_program(gl: &GL, vert: &WebGlShader, frag: &WebGlShader) -> Result<WebGlProgram, JsValue> {
    let program = gl.create_program().ok_or("unable to create program")?;
    gl.attach_shader(&program, vert);
    gl.attach_shader(&program, frag);
    gl.link_program(&program);
    if gl.get_program_parameter(&program, GL::LINK_STATUS).as_bool().unwrap_or(false) {
        Ok(program)
    } else {
        Err(JsValue::from_str(&gl.get_program_info_log(&program).unwrap_or_default()))
    }
}   