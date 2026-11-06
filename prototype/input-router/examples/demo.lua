return {
    enabled = true,
    dynamic_capture = true,
    require_foreground = true,
    targets = {
        jump = {kind="action", path="/PBZInput/InputActions/IA_Jump.IA_Jump", value_type="bool"},
        deflect = {kind="action", path="/PBZDeflectSystem/Input/IA_Deflect.IA_Deflect", value_type="bool"},
        stance = {kind="action", path="/PBZDeflectSystem/Input/IA_Stance.IA_Stance", value_type="bool"},
        special_dash = {kind="ability", tag="Ability.Action.Hun.SpecialDash", priority=-5,
            input_action="/PBZInput/InputActions/IA_Jump.IA_Jump"}
    },
    bindings = {
        {id="jump-f9", keys={"F9"}, trigger="press", outputs={"jump"}, consume=true},
        {id="guard-f10-press", keys={"F10"}, trigger="press", outputs={"deflect"}},
        {id="guard-f10-hold", keys={"F10"}, trigger="down", outputs={"stance"}, consume=true},
        {id="special-dash-f11", keys={"F11"}, trigger="press", outputs={"special_dash"}, consume=true},
        {id="guard-jump-chord", keys={"Gamepad_LeftShoulder","Gamepad_FaceButton_Bottom"},
            trigger="press", priority=100, consume={"Gamepad_FaceButton_Bottom"}, outputs={"jump"}}
    },
    hooks = {}
}
