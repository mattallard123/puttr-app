// Recording the coach's review on iPhone (puttr-app LessonRecording: ReplayKit in-app, app plus microphone).
// Stopping shows Apple's preview, where the coach saves it to the camera roll for Deliver.
#import <UIKit/UIKit.h>
#import <ReplayKit/ReplayKit.h>

extern UIViewController *UnityGetGLViewController(void);

@interface PuttrRecordPreview : NSObject <RPPreviewViewControllerDelegate>
@end

@implementation PuttrRecordPreview
- (void)previewControllerDidFinish:(RPPreviewViewController *)previewController
{
    [previewController dismissViewControllerAnimated:YES completion:nil];
}
@end

static PuttrRecordPreview *puttrRecordDelegate;

int PuttrRecord_Available(void) { return RPScreenRecorder.sharedRecorder.isAvailable ? 1 : 0; }
int PuttrRecord_Recording(void) { return RPScreenRecorder.sharedRecorder.isRecording ? 1 : 0; }

void PuttrRecord_Start(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        RPScreenRecorder *recorder = RPScreenRecorder.sharedRecorder;
        recorder.microphoneEnabled = YES;
        [recorder startRecordingWithHandler:^(NSError *error) { if (error) NSLog(@"Puttr record: %@", error); }];
    });
}

void PuttrRecord_Stop(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [RPScreenRecorder.sharedRecorder stopRecordingWithHandler:^(RPPreviewViewController *preview, NSError *error) {
            if (preview == nil) return;
            dispatch_async(dispatch_get_main_queue(), ^{
                if (puttrRecordDelegate == nil) puttrRecordDelegate = [PuttrRecordPreview new];
                preview.previewControllerDelegate = puttrRecordDelegate;
                preview.modalPresentationStyle = UIModalPresentationFullScreen;
                UIViewController *top = UnityGetGLViewController();
                while (top.presentedViewController) top = top.presentedViewController;
                [top presentViewController:preview animated:YES completion:nil];
            });
        }];
    });
}
