// Firebase Auth for the Unity app, through the same native SDK (and version, 10.29) the React
// Native app ships. That is the point of it: the SDK keeps the signed-in Player in the Keychain,
// so someone who updates from the old app is still signed in when this one first opens.
//
// Every call that takes time answers with UnitySendMessage(target, "OnFirebase", "<id>|ok|<value>")
// or "<id>|error|<code>|<message>"; the id is the caller's, so answers find their request.
#import <Foundation/Foundation.h>
@import FirebaseCore;
@import FirebaseAuth;

extern void UnitySendMessage(const char *object, const char *method, const char *message);

static NSString *PuttrString(const char *text) { return text ? [NSString stringWithUTF8String:text] : @""; }

static char *PuttrCopy(NSString *text)
{
    if (text == nil) return NULL;
    const char *utf8 = text.UTF8String;
    char *copy = (char *)malloc(strlen(utf8) + 1); // Unity frees a returned string
    strcpy(copy, utf8);
    return copy;
}

static void PuttrAnswer(NSString *target, NSString *request, NSString *value, NSError *error)
{
    NSString *message;
    if (error != nil)
    {
        NSString *code = error.userInfo[FIRAuthErrorUserInfoNameKey] ?: [NSString stringWithFormat:@"ERROR_%ld", (long)error.code];
        message = [NSString stringWithFormat:@"%@|error|%@|%@", request, code, error.localizedDescription ?: @""];
    }
    else message = [NSString stringWithFormat:@"%@|ok|%@", request, value ?: @""];
    dispatch_async(dispatch_get_main_queue(), ^{ UnitySendMessage(target.UTF8String, "OnFirebase", message.UTF8String); });
}

/// Starts Firebase from GoogleService-Info.plist in the app. Returns 1 when it is running.
int PuttrFirebase_Configure(void)
{
    // Without the plist, configure would stop the app: check first (exceptions are off in Unity's project).
    if ([[NSBundle mainBundle] pathForResource:@"GoogleService-Info" ofType:@"plist"] == nil)
    {
        NSLog(@"Puttr Firebase: GoogleService-Info.plist is not in the app");
        return 0;
    }
    if ([FIRApp defaultApp] == nil) [FIRApp configure];
    return [FIRApp defaultApp] != nil ? 1 : 0;
}

/// The signed-in Player's user id, or NULL. Straight after Configure this is whoever the
/// Keychain remembers, including a Player signed in by the app this one replaced.
char *PuttrFirebase_CurrentUid(void)
{
    if ([FIRApp defaultApp] == nil) return NULL;
    return PuttrCopy([FIRAuth auth].currentUser.uid);
}

void PuttrFirebase_GetIdToken(int force, const char *target, const char *request)
{
    NSString *to = PuttrString(target), *req = PuttrString(request);
    FIRUser *user = [FIRAuth auth].currentUser;
    if (user == nil) { PuttrAnswer(to, req, nil, [NSError errorWithDomain:@"Puttr" code:1 userInfo:@{FIRAuthErrorUserInfoNameKey: @"ERROR_NO_USER"}]); return; }
    [user getIDTokenForcingRefresh:force != 0 completion:^(NSString *token, NSError *error) { PuttrAnswer(to, req, token, error); }];
}

static void PuttrSignedIn(NSString *to, NSString *req, FIRAuthDataResult *result, NSError *error)
{
    BOOL isNew = result.additionalUserInfo.isNewUser;
    PuttrAnswer(to, req, result.user ? [NSString stringWithFormat:@"%@|%@", result.user.uid, isNew ? @"new" : @"known"] : nil, error);
}

void PuttrFirebase_SignInEmail(const char *email, const char *password, const char *target, const char *request)
{
    NSString *to = PuttrString(target), *req = PuttrString(request);
    [[FIRAuth auth] signInWithEmail:PuttrString(email) password:PuttrString(password) completion:^(FIRAuthDataResult *result, NSError *error) { PuttrSignedIn(to, req, result, error); }];
}

/// A new email and password account (EmailAuth "Create account": createUserWithEmailAndPassword).
void PuttrFirebase_CreateUser(const char *email, const char *password, const char *target, const char *request)
{
    NSString *to = PuttrString(target), *req = PuttrString(request);
    [[FIRAuth auth] createUserWithEmail:PuttrString(email) password:PuttrString(password) completion:^(FIRAuthDataResult *result, NSError *error) { PuttrSignedIn(to, req, result, error); }];
}

/// provider "apple.com": secret is the raw nonce. provider "google.com": secret is the access token.
void PuttrFirebase_SignInCredential(const char *provider, const char *idToken, const char *secret, const char *target, const char *request)
{
    NSString *to = PuttrString(target), *req = PuttrString(request), *kind = PuttrString(provider);
    FIRAuthCredential *credential;
    if ([kind isEqualToString:@"google.com"]) credential = [FIRGoogleAuthProvider credentialWithIDToken:PuttrString(idToken) accessToken:PuttrString(secret)];
    else credential = [FIROAuthProvider credentialWithProviderID:kind IDToken:PuttrString(idToken) rawNonce:PuttrString(secret)];
    [[FIRAuth auth] signInWithCredential:credential completion:^(FIRAuthDataResult *result, NSError *error) { PuttrSignedIn(to, req, result, error); }];
}

void PuttrFirebase_SignInCustomToken(const char *token, const char *target, const char *request)
{
    NSString *to = PuttrString(target), *req = PuttrString(request);
    [[FIRAuth auth] signInWithCustomToken:PuttrString(token) completion:^(FIRAuthDataResult *result, NSError *error) { PuttrSignedIn(to, req, result, error); }];
}

int PuttrFirebase_SignOut(void)
{
    NSError *error = nil;
    return [[FIRAuth auth] signOut:&error] ? 1 : 0;
}
