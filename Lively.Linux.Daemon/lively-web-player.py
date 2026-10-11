#!/usr/bin/env python3
import sys
import os
import signal
import argparse
import http.server
import socketserver
import threading
import json
import mimetypes
import warnings
import base64
import urllib.request

warnings.filterwarnings("ignore", category=DeprecationWarning)

# Configure GStreamer plugin path before initializing GTK/WebKit
def setup_gstreamer():
    gst_candidates = [
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "gst-plugins"),
        os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "gst-plugins"),
        os.path.expanduser("~/.local/share/lively-linux/gst-plugins"),
        "/home/auma/Projects/lively-linux/gst-plugins"
    ]
    for d in gst_candidates:
        full_d = os.path.abspath(d)
        if os.path.isdir(full_d):
            current = os.environ.get("GST_PLUGIN_PATH", "")
            if full_d not in current.split(":"):
                os.environ["GST_PLUGIN_PATH"] = f"{full_d}:{current}" if current else full_d
            break

setup_gstreamer()

# Register additional MIME types needed by Lively wallpapers
mimetypes.add_type('text/plain', '.frag')
mimetypes.add_type('text/plain', '.vert')
mimetypes.add_type('text/plain', '.glsl')
mimetypes.add_type('model/gltf-binary', '.glb')
mimetypes.add_type('model/gltf+json', '.gltf')
mimetypes.add_type('image/hdr', '.hdr')
mimetypes.add_type('audio/ogg', '.ogg')
mimetypes.add_type('video/webm', '.webm')

# Setup D-Bus with GLib mainloop before GTK initialization
HAS_DBUS = False
try:
    import dbus
    from dbus.mainloop.glib import DBusGMainLoop
    DBusGMainLoop(set_as_default=True)
    HAS_DBUS = True
except Exception as e:
    sys.stderr.write(f"D-Bus import notice: {e}\n")

import gi
gi.require_version('Gtk', '3.0')
gi.require_version('WebKit2', '4.1')
from gi.repository import Gtk, Gdk, WebKit2, GLib


class WallpaperHTTPHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, directory=None, **kwargs):
        super().__init__(*args, directory=directory, **kwargs)

    def log_message(self, format, *args):
        # Suppress standard HTTP request logging to keep daemon logs clean
        pass

    def do_OPTIONS(self):
        self.send_response(200)
        self.end_headers()

    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', '*')
        self.send_header('Cache-Control', 'no-cache, no-store, must-revalidate')
        super().end_headers()


def start_local_server(directory):
    server = socketserver.TCPServer(
        ('127.0.0.1', 0),
        lambda *args, **kwargs: WallpaperHTTPHandler(*args, directory=directory, **kwargs)
    )
    port = server.server_address[1]
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server, port


class MPRISManager:
    """Monitors Spotify and MPRIS media players and dispatches livelyCurrentTrack updates."""
    def __init__(self, webview):
        self.webview = webview
        self.art_cache = {}
        self.last_track_key = None
        self.lock = threading.Lock()
        self.bus = None

        if HAS_DBUS:
            try:
                self.bus = dbus.SessionBus()
                self.bus.add_signal_receiver(
                    self.on_properties_changed,
                    signal_name='PropertiesChanged',
                    dbus_interface='org.freedesktop.DBus.Properties',
                    path='/org/mpris/MediaPlayer2'
                )
                self.bus.add_signal_receiver(
                    self.on_name_owner_changed,
                    signal_name='NameOwnerChanged',
                    dbus_interface='org.freedesktop.DBus'
                )
            except Exception as ex:
                sys.stderr.write(f"MPRIS connection warning: {ex}\n")

        # Periodically check as a watchdog (every 2 seconds)
        GLib.timeout_add_seconds(2, self.periodic_check)
        # Immediate check after 500ms
        GLib.timeout_add(500, self.initial_check)

    def on_properties_changed(self, interface_name, changed_properties, invalidated_properties, **kwargs):
        if interface_name == 'org.mpris.MediaPlayer2.Player':
            self.trigger_async_update()

    def on_name_owner_changed(self, name, old_owner, new_owner, **kwargs):
        if name and name.startswith('org.mpris.MediaPlayer2.'):
            self.trigger_async_update()

    def initial_check(self):
        self.trigger_async_update()
        return False

    def periodic_check(self):
        self.trigger_async_update()
        return True

    def trigger_async_update(self):
        threading.Thread(target=self._query_and_push, daemon=True).start()

    def _query_and_push(self):
        if not self.bus:
            return

        with self.lock:
            spotify_cand = None
            other_cands = []
            try:
                for name in self.bus.list_names():
                    if name.startswith('org.mpris.MediaPlayer2.'):
                        try:
                            player = self.bus.get_object(name, '/org/mpris/MediaPlayer2')
                            props_iface = dbus.Interface(player, 'org.freedesktop.DBus.Properties')
                            status = str(props_iface.Get('org.mpris.MediaPlayer2.Player', 'PlaybackStatus'))
                            metadata = props_iface.Get('org.mpris.MediaPlayer2.Player', 'Metadata')
                            title = str(metadata.get('xesam:title', '')).strip()
                            artist_raw = metadata.get('xesam:artist', [''])
                            if isinstance(artist_raw, (list, tuple, dbus.Array)):
                                artist = ', '.join([str(a) for a in artist_raw if a])
                            else:
                                artist = str(artist_raw)
                            artist = artist.strip()
                            album = str(metadata.get('xesam:album', '')).strip()
                            art_url = str(metadata.get('mpris:artUrl', '')).strip()

                            info = {
                                'name': str(name),
                                'status': status,
                                'title': title,
                                'artist': artist,
                                'album': album,
                                'art_url': art_url
                            }
                            if 'spotify' in str(name).lower():
                                spotify_cand = info
                            else:
                                other_cands.append(info)
                        except Exception:
                            pass
            except Exception:
                return

            chosen = None
            # 1. Any player that is Playing and has a title
            for cand in ([spotify_cand] if spotify_cand else []) + other_cands:
                if cand and cand['status'] == 'Playing' and cand['title']:
                    chosen = cand
                    break

            # 2. If none is playing, prefer Spotify if it has a title and is not Stopped
            if not chosen and spotify_cand and spotify_cand['title'] and spotify_cand['status'] != 'Stopped':
                chosen = spotify_cand

            # 3. Any other player with title and not Stopped
            if not chosen:
                for cand in other_cands:
                    if cand['title'] and cand['status'] != 'Stopped':
                        chosen = cand
                        break

            if not chosen:
                if self.last_track_key != 'null':
                    self.last_track_key = 'null'
                    GLib.idle_add(self._dispatch_js, 'null', False)
                return

            track_key = f"{chosen['title']}::{chosen['artist']}::{chosen['art_url']}::{chosen['status']}"
            if track_key == self.last_track_key:
                return

            # Fetch artwork and convert to raw base64 string
            thumb_b64 = None
            art_url = chosen['art_url']
            if art_url:
                if art_url in self.art_cache:
                    thumb_b64 = self.art_cache[art_url]
                else:
                    try:
                        if art_url.startswith('file://'):
                            local_path = art_url[7:]
                            if os.path.exists(local_path):
                                with open(local_path, 'rb') as f:
                                    thumb_b64 = base64.b64encode(f.read()).decode('ascii')
                        elif art_url.startswith('http://') or art_url.startswith('https://'):
                            req = urllib.request.Request(art_url, headers={'User-Agent': 'Mozilla/5.0'})
                            with urllib.request.urlopen(req, timeout=3) as resp:
                                thumb_b64 = base64.b64encode(resp.read()).decode('ascii')
                        if thumb_b64:
                            self.art_cache[art_url] = thumb_b64
                    except Exception as e:
                        sys.stderr.write(f"MPRIS art fetch warning: {e}\n")

            track_obj = {
                'Title': chosen['title'],
                'Artist': chosen['artist'],
                'Album': chosen['album'],
                'Thumbnail': thumb_b64
            }
            track_json = json.dumps(track_obj)
            self.last_track_key = track_key
            is_playing = (chosen['status'] == 'Playing')
            GLib.idle_add(self._dispatch_js, json.dumps(track_json), is_playing)

    def _dispatch_js(self, data_json_str, is_playing):
        js = (
            f"if (typeof window.__livelyMediaState === 'object') {{ "
            f"  window.__livelyMediaState.isPlaying = {'true' if is_playing else 'false'}; "
            f"}} "
            f"if (typeof livelyCurrentTrack === 'function') {{ "
            f"  try {{ livelyCurrentTrack({data_json_str}); }} catch(e) {{ console.error('livelyCurrentTrack error:', e); }} "
            f"}}"
        )
        self.webview.run_javascript(js, None, None, None)


def main():
    parser = argparse.ArgumentParser(description="Lively Linux Web Wallpaper Player")
    parser.add_argument("path", help="Path to index.html or URL")
    parser.add_argument("--monitor", help="Target monitor connector or model name", default=None)
    args = parser.parse_args()

    # Ensure display environment if missing or empty
    if not os.environ.get("WAYLAND_DISPLAY") and not os.environ.get("DISPLAY"):
        os.environ["WAYLAND_DISPLAY"] = "wayland-0"
        os.environ["DISPLAY"] = ":0"
    if not os.environ.get("XDG_RUNTIME_DIR"):
        os.environ["XDG_RUNTIME_DIR"] = "/run/user/1000"

    success, _ = Gtk.init_check([])
    if not success:
        sys.stderr.write("Failed to initialize GTK display connection.\n")
        sys.exit(1)

    raw_path = args.path
    server = None
    wallpaper_dir = None

    if not (raw_path.startswith("http://") or raw_path.startswith("https://")):
        abs_path = os.path.abspath(raw_path)
        if os.path.isdir(abs_path):
            wallpaper_dir = abs_path
            entry_file = "index.html"
            if not os.path.exists(os.path.join(wallpaper_dir, entry_file)):
                for f in os.listdir(wallpaper_dir):
                    if f.endswith(".html"):
                        entry_file = f
                        break
        else:
            wallpaper_dir = os.path.dirname(abs_path)
            entry_file = os.path.basename(abs_path)

        server, port = start_local_server(wallpaper_dir)
        uri = f"http://127.0.0.1:{port}/{entry_file}"
    else:
        uri = raw_path

    # Set program name so Wayland app_id is 'lively-wallpaper'
    GLib.set_prgname("lively-wallpaper")
    GLib.set_application_name("lively-wallpaper")

    win = Gtk.Window(type=Gtk.WindowType.TOPLEVEL)
    win.set_title("lively-wallpaper")
    win.set_decorated(False)
    win.set_skip_taskbar_hint(True)
    win.set_skip_pager_hint(True)
    win.set_keep_below(True)
    win.set_accept_focus(False)
    win.set_focus_on_map(False)

    # Position on target display if possible
    gdk_display = Gdk.Display.get_default()
    if gdk_display:
        target_monitor = None
        num_monitors = gdk_display.get_n_monitors()
        if args.monitor:
            for i in range(num_monitors):
                mon = gdk_display.get_monitor(i)
                model = mon.get_model()
                if model and args.monitor.lower() in model.lower():
                    target_monitor = mon
                    break
        if not target_monitor and num_monitors > 0:
            target_monitor = gdk_display.get_primary_monitor() or gdk_display.get_monitor(0)

        if target_monitor:
            geom = target_monitor.get_geometry()
            win.move(geom.x, geom.y)
            win.set_default_size(geom.width, geom.height)

    win.maximize()

    # WebKit configuration
    settings = WebKit2.Settings()
    settings.set_enable_webgl(True)
    settings.set_enable_javascript(True)
    settings.set_allow_file_access_from_file_urls(True)
    settings.set_allow_universal_access_from_file_urls(True)
    settings.set_media_playback_allows_inline(True)
    settings.set_enable_write_console_messages_to_stdout(True)

    # Inject CSS to hide scrollbars and remove margins
    ucm = WebKit2.UserContentManager()
    css = (
        "::-webkit-scrollbar { display: none !important; } "
        "html, body { overflow: hidden !important; margin: 0 !important; padding: 0 !important; width: 100% !important; height: 100% !important; }"
    )
    style_sheet = WebKit2.UserStyleSheet(
        css,
        WebKit2.UserContentInjectedFrames.ALL_FRAMES,
        WebKit2.UserStyleLevel.USER,
        None,
        None
    )
    ucm.add_style_sheet(style_sheet)

    # Inject media audio listener animator script
    media_anim_js = (
        "window.__livelyMediaState = { isPlaying: false }; "
        "(function() { "
        "  let phase = 0; "
        "  function updateAudioSim() { "
        "    requestAnimationFrame(updateAudioSim); "
        "    if (!window.__livelyMediaState || !window.__livelyMediaState.isPlaying) return; "
        "    if (typeof livelyAudioListener !== 'function') return; "
        "    phase += 0.08; "
        "    const bins = new Array(64); "
        "    for (let i = 0; i < 64; i++) { "
        "      const v = Math.sin(phase + i * 0.15) * 0.5 + 0.5; "
        "      const v2 = Math.sin(phase * 1.7 + i * 0.3) * 0.3; "
        "      bins[i] = Math.max(0, Math.min(1, (v + v2) * (1 - i / 70))); "
        "    } "
        "    livelyAudioListener(bins); "
        "  } "
        "  requestAnimationFrame(updateAudioSim); "
        "})();"
    )
    anim_script = WebKit2.UserScript(
        media_anim_js,
        WebKit2.UserContentInjectedFrames.ALL_FRAMES,
        WebKit2.UserScriptInjectionTime.START,
        None,
        None
    )
    ucm.add_script(anim_script)

    view = WebKit2.WebView.new_with_user_content_manager(ucm)
    view.set_settings(settings)

    # Background color black
    rgba = Gdk.RGBA()
    rgba.parse("rgb(0,0,0)")
    view.set_background_color(rgba)

    # MPRIS Media Monitor
    mpris_manager = MPRISManager(view)

    # Dispatch LivelyProperties on load finish
    def on_load_changed(webview, event):
        if event != WebKit2.LoadEvent.FINISHED:
            return

        if not wallpaper_dir:
            return

        props_to_send = []
        prop_file = os.path.join(wallpaper_dir, "LivelyProperties.json")
        if os.path.exists(prop_file):
            try:
                with open(prop_file, "r", encoding="utf-8") as f:
                    props = json.load(f)

                for k, v in props.items():
                    if isinstance(v, dict):
                        ptype = v.get("type")
                        pval = v.get("value")
                        if pval is None:
                            continue
                        if ptype == "folderDropdown":
                            folder = v.get("folder", "")
                            if folder and not str(pval).startswith(folder + "/"):
                                pval = f"{folder}/{pval}"
                        props_to_send.append((k, pval))
            except Exception as e:
                sys.stderr.write(f"Failed to read LivelyProperties.json: {e}\n")

        js_statements = []
        for k, pval in props_to_send:
            js_statements.append(
                f"if (typeof livelyPropertyListener === 'function') {{ "
                f"try {{ livelyPropertyListener({json.dumps(k)}, {json.dumps(pval)}); }} catch(e) {{ console.error(e); }} "
                f"}}"
            )

        full_js = (
            "(() => { "
            "  const dispatch = () => { " + "; ".join(js_statements) + " }; "
            "  dispatch(); "
            "  setTimeout(dispatch, 200); "
            "  setTimeout(dispatch, 800); "
            "  document.addEventListener('sceneLoaded', dispatch, { once: true }); "
            "})();"
        )
        webview.run_javascript(full_js, None, None, None)
        # Immediately re-trigger MPRIS state dispatch to ensure fresh wallpaper has track
        mpris_manager.trigger_async_update()

    view.connect("load-changed", on_load_changed)

    win.add(view)
    view.load_uri(uri)
    win.show_all()

    def cleanup():
        nonlocal server
        if server:
            try:
                server.shutdown()
                server.server_close()
            except Exception:
                pass
            server = None

    # Handle clean termination
    def handle_signal(sig, frame):
        cleanup()
        GLib.idle_add(Gtk.main_quit)

    signal.signal(signal.SIGINT, handle_signal)
    signal.signal(signal.SIGTERM, handle_signal)
    win.connect("destroy", lambda w: (cleanup(), Gtk.main_quit()))

    # Run GTK main loop
    try:
        Gtk.main()
    finally:
        cleanup()


if __name__ == "__main__":
    main()
