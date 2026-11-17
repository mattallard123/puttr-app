namespace PuttrApp
{
    // The ambient loop under each 3D world (Soundly library slots, Audio/Library/<slot>_N.wav).
    public sealed partial class OhOneGame { public override string Bed => "pub_loop"; }
    public sealed partial class LeapfrogGame { public override string Bed => "pond_loop"; }
    public sealed partial class HighSideGame { public override string Bed => "wind_loop"; }
}
