// Push on iOS, as puttr-app's @react-native-firebase/messaging: ask permission (alert, badge, sound),
// register with APNs, and get the FCM token (Firebase maps the APNs token itself). Told to Unity:
//   OnPushToken      <fcm token>, or "" if there is none
//   OnPushOpened     the tapped notification's data, as JSON (a tap that launched the app waits for Unity)
//   OnPushForeground {title, body, data} for a push that arrives while the app is open (no system banner:
//                    the app shows its own toast, as react-native-notifier does)
#import <UIKit/UIKit.h>
#import <UserNotifications/UserNotifications.h>
@import FirebaseMessaging;

extern void UnitySendMessage(const char *object, const char *method, const char *message);

@interface PuttrPush : NSObject <UNUserNotificationCenterDelegate>
@property (nonatomic, copy) NSString *target;
@property (nonatomic, copy) NSString *pendingTap;
@end

@implementation PuttrPush

+ (instancetype)shared
{
    static PuttrPush *one;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ one = [PuttrPush new]; });
    return one;
}

// As early as possible, so a tap that launches the app is not missed.
+ (void)load
{
    dispatch_async(dispatch_get_main_queue(), ^{ UNUserNotificationCenter.currentNotificationCenter.delegate = PuttrPush.shared; });
}

- (void)send:(NSString *)method message:(NSString *)message
{
    if (self.target.length == 0) return;
    NSString *to = self.target, *m = message ?: @"";
    dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(to.UTF8String, method.UTF8String, m.UTF8String); });
}

static NSString *Json(id object)
{
    if (object == nil || ![NSJSONSerialization isValidJSONObject:object]) return @"{}";
    NSData *data = [NSJSONSerialization dataWithJSONObject:object options:0 error:nil];
    return data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{}";
}

// Everything in the push but Apple's own keys: what the API put in data.
static NSDictionary *Data(NSDictionary *userInfo)
{
    NSMutableDictionary *data = [NSMutableDictionary dictionary];
    for (id key in userInfo)
    {
        NSString *k = [key description];
        if ([k isEqualToString:@"aps"] || [k hasPrefix:@"gcm."] || [k hasPrefix:@"google."] || [k hasPrefix:@"fcm_"]) continue;
        id v = userInfo[key];
        data[k] = [v isKindOfClass:NSString.class] ? v : [v description];
    }
    return data;
}

- (void)fetchToken:(int)tries
{
    [[FIRMessaging messaging] tokenWithCompletion:^(NSString *token, NSError *error) {
        if (token.length > 0) { [self send:@"OnPushToken" message:token]; return; }
        // Until APNs has given its token, FCM has none: try again for a while.
        if (tries > 0) dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(2 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{ [self fetchToken:tries - 1]; });
        else [self send:@"OnPushToken" message:@""];
    }];
}

- (void)registerFor:(NSString *)target
{
    self.target = target;
    UNUserNotificationCenter *center = UNUserNotificationCenter.currentNotificationCenter;
    center.delegate = self;
    [center requestAuthorizationWithOptions:(UNAuthorizationOptionAlert | UNAuthorizationOptionBadge | UNAuthorizationOptionSound) completionHandler:^(BOOL granted, NSError *error) {
        dispatch_async(dispatch_get_main_queue(), ^{
            [UIApplication.sharedApplication registerForRemoteNotifications];
            [self fetchToken:8];
        });
    }];
    if (self.pendingTap.length > 0) { [self send:@"OnPushOpened" message:self.pendingTap]; self.pendingTap = nil; }
}

- (void)userNotificationCenter:(UNUserNotificationCenter *)center willPresentNotification:(UNNotification *)notification withCompletionHandler:(void (^)(UNNotificationPresentationOptions))completionHandler
{
    UNNotificationContent *c = notification.request.content;
    [self send:@"OnPushForeground" message:Json(@{ @"title": c.title ?: @"", @"body": c.body ?: @"", @"data": Data(c.userInfo) })];
    completionHandler(UNNotificationPresentationOptionNone);
}

- (void)userNotificationCenter:(UNUserNotificationCenter *)center didReceiveNotificationResponse:(UNNotificationResponse *)response withCompletionHandler:(void (^)(void))completionHandler
{
    NSString *data = Json(Data(response.notification.request.content.userInfo));
    if (self.target.length > 0) [self send:@"OnPushOpened" message:data];
    else self.pendingTap = data;
    completionHandler();
}

@end

void PuttrPush_Register(const char *target)
{
    NSString *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{ [PuttrPush.shared registerFor:to]; });
}

void PuttrPush_Badge(int count)
{
    dispatch_async(dispatch_get_main_queue(), ^{ UIApplication.sharedApplication.applicationIconBadgeNumber = count; });
}
