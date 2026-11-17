// Sign in with Apple, and a web sign-in sheet (used for Google), for the Unity app.
//
// Both hand a string back to Unity with UnitySendMessage:
//   OnAppleToken   the identity token (a JWT) for Firebase
//   OnWebAuth      the callback URL the web sign-in ended on
//   OnSignInError  why it stopped; "cancelled" when the Player closed the sheet
#import <UIKit/UIKit.h>
#import <AuthenticationServices/AuthenticationServices.h>

extern void UnitySendMessage(const char *object, const char *method, const char *message);

@interface PuttrSignIn : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding, ASWebAuthenticationPresentationContextProviding>
@property (nonatomic, copy) NSString *target;
@property (nonatomic, strong) ASWebAuthenticationSession *session;
@end

@implementation PuttrSignIn

+ (instancetype)shared
{
    static PuttrSignIn *one;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ one = [PuttrSignIn new]; });
    return one;
}

- (void)send:(NSString *)method message:(NSString *)message
{
    if (self.target.length == 0) return;
    UnitySendMessage(self.target.UTF8String, method.UTF8String, (message ?: @"").UTF8String);
}

- (UIWindow *)window
{
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;
        for (UIWindow *window in ((UIWindowScene *)scene).windows) if (window.isKeyWindow) return window;
    }
    return UIApplication.sharedApplication.windows.firstObject;
}

// ── Apple ────────────────────────────────────────────────────────────────

- (void)appleWithNonce:(NSString *)hashedNonce
{
    ASAuthorizationAppleIDRequest *request = [[ASAuthorizationAppleIDProvider new] createRequest];
    request.requestedScopes = @[ASAuthorizationScopeEmail, ASAuthorizationScopeFullName];
    request.nonce = hashedNonce;
    ASAuthorizationController *controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
    controller.delegate = self;
    controller.presentationContextProvider = self;
    [controller performRequests];
}

- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithAuthorization:(ASAuthorization *)authorization
{
    if (![authorization.credential isKindOfClass:ASAuthorizationAppleIDCredential.class]) { [self send:@"OnSignInError" message:@"Apple gave no credential."]; return; }
    ASAuthorizationAppleIDCredential *credential = (ASAuthorizationAppleIDCredential *)authorization.credential;
    NSString *token = credential.identityToken ? [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding] : nil;
    if (token.length == 0) { [self send:@"OnSignInError" message:@"Apple gave no identity token."]; return; }
    [self send:@"OnAppleToken" message:token];
}

- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithError:(NSError *)error
{
    BOOL cancelled = [error.domain isEqualToString:ASAuthorizationErrorDomain] && error.code == ASAuthorizationErrorCanceled;
    [self send:@"OnSignInError" message:cancelled ? @"cancelled" : error.localizedDescription];
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller { return [self window]; }

// ── A web sign-in sheet ──────────────────────────────────────────────────

- (void)webAuth:(NSString *)url scheme:(NSString *)scheme
{
    __weak PuttrSignIn *weak = self;
    self.session = [[ASWebAuthenticationSession alloc] initWithURL:[NSURL URLWithString:url] callbackURLScheme:scheme completionHandler:^(NSURL *callback, NSError *error) {
        PuttrSignIn *me = weak;
        me.session = nil;
        if (callback != nil) { [me send:@"OnWebAuth" message:callback.absoluteString]; return; }
        BOOL cancelled = [error.domain isEqualToString:ASWebAuthenticationSessionErrorDomain] && error.code == ASWebAuthenticationSessionErrorCodeCanceledLogin;
        [me send:@"OnSignInError" message:cancelled ? @"cancelled" : (error.localizedDescription ?: @"Sign-in did not finish.")];
    }];
    self.session.presentationContextProvider = self;
    if (![self.session start]) { self.session = nil; [self send:@"OnSignInError" message:@"Could not open the sign-in page."]; }
}

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession *)session { return [self window]; }

@end

void PuttrSignIn_Apple(const char *hashedNonce, const char *target)
{
    NSString *nonce = [NSString stringWithUTF8String:hashedNonce ?: ""], *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrSignIn.shared.target = to;
        [PuttrSignIn.shared appleWithNonce:nonce];
    });
}

void PuttrSignIn_WebAuth(const char *url, const char *scheme, const char *target)
{
    NSString *address = [NSString stringWithUTF8String:url ?: ""], *callback = [NSString stringWithUTF8String:scheme ?: ""], *to = [NSString stringWithUTF8String:target ?: ""];
    dispatch_async(dispatch_get_main_queue(), ^{
        PuttrSignIn.shared.target = to;
        [PuttrSignIn.shared webAuth:address scheme:callback];
    });
}
