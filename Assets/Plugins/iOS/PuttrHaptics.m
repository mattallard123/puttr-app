// A short tap from the Taptic Engine (Haptics.cs), where puttr-app used react-native-haptic-feedback.
// kind: 0 light, 1 medium, 2 heavy (impacts); 3 success, 4 warning, 5 error (notifications).
// A phone with haptics turned off in Settings, or an iPad (no Taptic Engine), simply does nothing.
#import <UIKit/UIKit.h>
#import <TargetConditionals.h>

void PuttrHaptics_Play(int kind)
{
#if TARGET_OS_IOS
    // The generators belong to the main thread. Unity calls from it already; the hop covers any caller that does not.
    dispatch_async(dispatch_get_main_queue(), ^{
        if (kind >= 3)
        {
            UINotificationFeedbackType type = kind == 3 ? UINotificationFeedbackTypeSuccess
                : kind == 4 ? UINotificationFeedbackTypeWarning : UINotificationFeedbackTypeError;
            UINotificationFeedbackGenerator *generator = [[UINotificationFeedbackGenerator alloc] init];
            [generator prepare];
            [generator notificationOccurred:type];
        }
        else
        {
            UIImpactFeedbackStyle style = kind == 0 ? UIImpactFeedbackStyleLight
                : kind == 2 ? UIImpactFeedbackStyleHeavy : UIImpactFeedbackStyleMedium;
            UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
            [generator prepare];
            [generator impactOccurred];
        }
    });
#endif
}
