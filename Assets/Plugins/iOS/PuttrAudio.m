// Game sounds play with the ring/silent switch on silent (Matt: you should hear the game). Unity's default audio
// session is Ambient, which the switch mutes; Playback is not muted. MixWithOthers keeps the Player's own music
// playing under the game. While the camera records (Agora sets PlayAndRecord) the session is left as it is.
#import <AVFoundation/AVFoundation.h>

void PuttrAudio_Play(void)
{
    AVAudioSession *session = [AVAudioSession sharedInstance];
    if ([session.category isEqualToString:AVAudioSessionCategoryPlayAndRecord]) return;
    NSError *error = nil;
    [session setCategory:AVAudioSessionCategoryPlayback withOptions:AVAudioSessionCategoryOptionMixWithOthers error:&error];
    if (error == nil) [session setActive:YES error:nil];
}

/// The app's sound started again: the playback category, the session active, and Unity's sound engine told to restart
/// (it restarts on an "interruption ended" notice, as Unity's own keyboard code does). For when the sound clock has
/// stopped: nothing plays, and anything waiting on a sound to finish waits for ever.
void PuttrAudio_Restore(void)
{
    AVAudioSession *session = [AVAudioSession sharedInstance];
    [session setCategory:AVAudioSessionCategoryPlayback withOptions:AVAudioSessionCategoryOptionMixWithOthers error:nil];
    [session setActive:YES error:nil];
    [[NSNotificationCenter defaultCenter] postNotificationName:AVAudioSessionInterruptionNotification object:session
        userInfo:@{AVAudioSessionInterruptionTypeKey: @(AVAudioSessionInterruptionTypeEnded)}];
}

/// What the phone's sound is set to, for the log: category, other apps playing, the volume, where it comes out.
const char *PuttrAudio_State(void)
{
    AVAudioSession *session = [AVAudioSession sharedInstance];
    NSMutableArray *outs = [NSMutableArray array];
    for (AVAudioSessionPortDescription *port in session.currentRoute.outputs) [outs addObject:port.portType];
    NSString *text = [NSString stringWithFormat:@"category %@, options %lu, other audio %d, volume %.2f, out %@",
        session.category, (unsigned long)session.categoryOptions, session.isOtherAudioPlaying ? 1 : 0, session.outputVolume, [outs componentsJoinedByString:@"+"]];
    return strdup(text.UTF8String);
}
