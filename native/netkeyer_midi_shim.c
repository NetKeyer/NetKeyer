/*
 * netkeyer_midi_shim.c
 *
 * Thin C wrapper around the libremidi v5 C API.  Exposes 6 simple functions
 * with no struct/union marshaling so that .NET can P/Invoke them safely.
 *
 * Build: see CMakeLists.txt in this directory.
 */

#include <libremidi/libremidi-c.h>

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* ---- Export macro ---- */
#ifdef _WIN32
  #ifdef NKM_EXPORTS
    #define NKM_API __declspec(dllexport)
  #else
    #define NKM_API __declspec(dllimport)
  #endif
#else
  #define NKM_API
#endif

/* ---- Shared error/warning callbacks ---- */

static void on_error_cb(void* ctx, const char* msg, size_t len,
                        const void* source_location)
{
    (void)ctx; (void)source_location;
    fprintf(stderr, "[netkeyer_midi_shim] ERROR: %.*s\n", (int)len, msg);
    fflush(stderr);
}

static void on_warning_cb(void* ctx, const char* msg, size_t len,
                          const void* source_location)
{
    (void)ctx; (void)source_location;
    fprintf(stderr, "[netkeyer_midi_shim] WARNING: %.*s\n", (int)len, msg);
    fflush(stderr);
}

/* ---- Observer (port enumeration) ---- */

typedef struct {
    libremidi_midi_observer_handle* obs;
    libremidi_midi_in_port**        ports;
    int                             count;
    int                             capacity;
    enum libremidi_api              api; /* backend actually selected */
} nkm_observer_t;

static void observer_port_added(void* ctx, const libremidi_midi_in_port* port)
{
    nkm_observer_t* o = (nkm_observer_t*)ctx;
    if (o->count >= o->capacity) {
        int newcap = o->capacity ? o->capacity * 2 : 8;
        libremidi_midi_in_port** p =
            realloc(o->ports, (size_t)newcap * sizeof(*p));
        if (!p) return;
        o->ports    = p;
        o->capacity = newcap;
    }
    libremidi_midi_in_port* clone = NULL;
    if (libremidi_midi_in_port_clone(port, &clone) == 0)
        o->ports[o->count++] = clone;
}

/* Try to create an observer using the specified api enum value.
 * Returns 0 on success, non-zero on failure.
 * On success, o->obs is set and o->ports is populated. */
static int try_observer(nkm_observer_t* o, enum libremidi_api api)
{
    /* Reset port list for a fresh attempt */
    for (int i = 0; i < o->count; i++)
        libremidi_midi_in_port_free(o->ports[i]);
    o->count = 0;
    if (o->obs)
    {
        libremidi_midi_observer_free(o->obs);
        o->obs = NULL;
    }

    libremidi_observer_configuration obs_cfg;
    libremidi_midi_observer_configuration_init(&obs_cfg);
    obs_cfg.on_error.callback   = on_error_cb;
    obs_cfg.on_warning.callback = on_warning_cb;
    obs_cfg.track_hardware      = true;
    obs_cfg.track_virtual       = true;
    /* Do NOT set input_added or notify_in_constructor.
     * When no callbacks are registered, libremidi's has_callbacks() returns
     * false and the CoreMIDI backend skips MIDIClientCreate() in finish_init().
     * MIDIClientCreate() can fail on macOS when called from a .NET thread
     * that has no CFRunLoop, or in a signed/sandboxed context without MIDI
     * entitlements.  We do NOT need a MIDIClient for enumeration: after the
     * observer is created we call libremidi_midi_observer_enumerate_input_ports()
     * which invokes get_input_ports() → MIDIGetNumberOfSources() directly,
     * with no client required.  The ALSA and WinMM backends are unaffected
     * because their get_input_ports() also operates without a client object. */

    libremidi_api_configuration api_cfg;
    libremidi_midi_api_configuration_init(&api_cfg);
    api_cfg.api                = api;
    api_cfg.configuration_type = Observer;

    if (libremidi_midi_observer_new(&obs_cfg, &api_cfg, &o->obs) != 0)
        return -1;

    /* Populate the port cache via direct system query (no MIDIClient needed). */
    libremidi_midi_observer_enumerate_input_ports(o->obs, o, observer_port_added);

    return 0;
}

NKM_API void* nkm_create_observer(void)
{
    nkm_observer_t* o = calloc(1, sizeof(nkm_observer_t));
    if (!o) return NULL;

    /* Try the platform default first. */
    if (try_observer(o, UNSPECIFIED) != 0) {
        fprintf(stderr, "[netkeyer_midi_shim] UNSPECIFIED observer failed\n");
        fflush(stderr);
        free(o->ports);
        free(o);
        return NULL;
    }

#ifdef __linux__
    /* On Linux, UNSPECIFIED may pick PipeWire, which only exposes devices that
     * have been explicitly bridged into PipeWire's MIDI graph.  If we found no
     * ports, fall back to ALSA Sequencer which sees all kernel MIDI clients. */
    if (o->count == 0) {
        fprintf(stderr, "[netkeyer_midi_shim] UNSPECIFIED found 0 ports, retrying with ALSA_SEQ\n");
        fflush(stderr);
        if (try_observer(o, ALSA_SEQ) != 0) {
            fprintf(stderr, "[netkeyer_midi_shim] ALSA_SEQ observer also failed\n");
            fflush(stderr);
            free(o->ports);
            free(o);
            return NULL;
        }
        o->api = ALSA_SEQ;
    } else {
        o->api = UNSPECIFIED;
    }
#else
    o->api = UNSPECIFIED;
#endif

    return o;
}

NKM_API void nkm_free_observer(void* handle)
{
    if (!handle) return;
    nkm_observer_t* o = (nkm_observer_t*)handle;
    for (int i = 0; i < o->count; i++)
        libremidi_midi_in_port_free(o->ports[i]);
    free(o->ports);
    libremidi_midi_observer_free(o->obs);
    free(o);
}

NKM_API int nkm_input_count(void* handle)
{
    if (!handle) return -1;
    return ((nkm_observer_t*)handle)->count;
}

NKM_API int nkm_input_name(void* handle, int index, char* buf, int buf_len)
{
    if (!handle || !buf || buf_len <= 0) return -1;
    nkm_observer_t* o = (nkm_observer_t*)handle;
    if (index < 0 || index >= o->count) return -1;

    const char* name     = NULL;
    size_t      name_len = 0;
    if (libremidi_midi_in_port_name(o->ports[index], &name, &name_len) != 0 || !name) {
        buf[0] = '\0';
        return -1;
    }

    size_t copy_len = (name_len < (size_t)(buf_len - 1)) ? name_len : (size_t)(buf_len - 1);
    memcpy(buf, name, copy_len);
    buf[copy_len] = '\0';
    return 0;
}

/* ---- Input (open / close) ---- */

typedef void (*nkm_message_cb)(void* ctx, const uint8_t* data, int len);

typedef struct {
    libremidi_midi_in_handle* in;
    nkm_message_cb            user_cb;
    void*                     user_ctx;
} nkm_input_t;

static void midi_in_callback(void* ctx, libremidi_timestamp ts,
                              const libremidi_midi1_symbol* data, size_t len)
{
    (void)ts;
    nkm_input_t* inp = (nkm_input_t*)ctx;
    if (inp->user_cb)
        inp->user_cb(inp->user_ctx, (const uint8_t*)data, (int)len);
}

NKM_API void* nkm_open_input(void* obs_handle, int index,
                              nkm_message_cb cb, void* user_ctx)
{
    if (!obs_handle) return NULL;
    nkm_observer_t* o = (nkm_observer_t*)obs_handle;
    if (index < 0 || index >= o->count) return NULL;

    nkm_input_t* inp = malloc(sizeof(nkm_input_t));
    if (!inp) return NULL;
    inp->in       = NULL;
    inp->user_cb  = cb;
    inp->user_ctx = user_ctx;

    libremidi_midi_configuration in_cfg;
    libremidi_midi_configuration_init(&in_cfg);
    in_cfg.version                   = MIDI1;
    in_cfg.in_port                   = o->ports[index];
    in_cfg.on_midi1_message.context  = inp;
    in_cfg.on_midi1_message.callback = midi_in_callback;
    in_cfg.ignore_sysex              = true;
    in_cfg.ignore_timing             = true;
    in_cfg.ignore_sensing            = true;
    in_cfg.on_error.callback         = on_error_cb;
    in_cfg.on_warning.callback       = on_warning_cb;

    libremidi_api_configuration api_cfg;
    libremidi_midi_api_configuration_init(&api_cfg);
    api_cfg.api                = o->api; /* match the backend the observer used */
    api_cfg.configuration_type = Input;

    if (libremidi_midi_in_new(&in_cfg, &api_cfg, &inp->in) != 0) {
        fprintf(stderr, "[netkeyer_midi_shim] libremidi_midi_in_new failed\n");
        fflush(stderr);
        free(inp);
        return NULL;
    }
    return inp;
}

NKM_API void nkm_close_input(void* handle)
{
    if (!handle) return;
    nkm_input_t* inp = (nkm_input_t*)handle;
    libremidi_midi_in_free(inp->in);
    free(inp);
}
