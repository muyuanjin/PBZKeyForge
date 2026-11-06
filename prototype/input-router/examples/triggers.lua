return {
    targets = {
        jump = {kind="action", path="/PBZInput/InputActions/IA_Jump.IA_Jump", value_type="bool"},
        dash = {kind="ability", tag="Ability.Action.Hun.SpecialDash", priority=-5},
        move = {kind="action", path="/Game/PBZContent/Input/InputActions/IA_MoveInput.IA_MoveInput", clamp=1},
        event = {kind="event", tag="GameplayEvent.Ability.Action.Deflect.Player.KeyUp"},
        function_call = {kind="function", receiver="pawn", path="/Script/Engine.Character:Jump"}
    },
    bindings = {
        {id="tap", keys={"K"}, trigger="tap", seconds=0.2, outputs={"jump"}},
        {id="hold", keys={"H"}, trigger="hold", seconds=0.5, outputs={"jump"}},
        {id="repeat", keys={"J"}, trigger="pulse", interval=0.5, outputs={"jump"}},
        {id="triple", keys={"L"}, trigger="multi_tap", count=3, interval=0.3, outputs={"dash"}},
        {id="ordered", trigger="sequence", steps={"I","O","I"}, interval=0.5, outputs={"function_call"}},
        {id="chord", keys={"LeftControl","U"}, trigger="press", consume={"U"}, outputs={"jump"}},
        {id="release", keys={"P"}, trigger="release", outputs={"event"}},
        {id="axis", axis="Gamepad_LeftX", trigger="axis", threshold=0.15,
            outputs={{target="move", value={x=0,y=1,z=0}}}}
    }
}
