import io, json

p = 'girs/overlays/fixups.json'
lines = io.open(p, encoding='utf-8').read().split('\n')


def entry(key, value, indent=4):
    return ' ' * indent + json.dumps(key, ensure_ascii=False) + ': ' + json.dumps(
        value, ensure_ascii=False).replace('{"', '{ "').replace('"}', '" }').replace('", "', '", "') + ','


annotations = [
 ("GstBase.BaseParse::sink_event#event", {"transfer": "full", "$comment": "gstbaseparse.c:1264-1273 - gst_base_parse_sink_event calls the slot and answers what it answered without unreffing the event, and the default implementation at :1287 either unrefs it, hands it to gst_pad_push_event, which consumes it, or stores it in priv->pending_events. The slot therefore owns the reference it is handed; the gir says none."}),
 ("GstBase.BaseParse::src_event#event", {"transfer": "full", "$comment": "gstbaseparse.c:1757-1775 - the NULL slot path is the one that unrefs the event, and the default at :1794 hands it to gst_pad_event_default, which consumes it. The gir says none."}),
 ("GstBase.BaseParse::convert#dest_value", {"direction": "out", "$comment": "gstbaseparse.c - gst_base_parse_convert_default writes the converted value through the pointer and never reads what was there. The gir gives the bare gint64* no direction at all, which leaves the slot with a shape the planner cannot project."}),
 ("GstAudio.AudioDecoder::sink_event#event", {"transfer": "full", "$comment": "gstaudiodecoder.c:2604-2608 - a NULL slot unrefs the event and answers FALSE, and the default implementation at :2370 consumes it on every path. The gir says none."}),
 ("GstAudio.AudioDecoder::src_event#event", {"transfer": "full", "$comment": "gstaudiodecoder.c:2749-2753 - the same shape as the sink event above, with the default at :2671."}),
 ("GstAudio.AudioDecoder::handle_frame#buffer", {"nullable": True, "$comment": "gstaudiodecoder.c:1656 calls the slot with the parsed data, and the drain at :1750 calls it with NULL to tell the subclass that the stream ended; gstaudiodecoder.h:196-199 says so in words. The gir carries no nullable."}),
 ("GstAudio.AudioDecoder::pre_push#buffer", {"direction": "inout", "transfer": "full", "$comment": "gstaudiodecoder.c:1025-1035 - the caller owns buf before the call and passes its address; afterwards it pushes what it finds there, or unrefs it when the slot answered a failure or left NULL. The reference travels into the slot and back out, which the gir spells as a plain in parameter of transfer none."}),
 ("GstAudio.AudioEncoder::sink_event#event", {"transfer": "full", "$comment": "gstaudioencoder.c:1772-1776 - a NULL slot unrefs the event and answers FALSE, and the default implementation at :1591 consumes it. The gir says none."}),
 ("GstAudio.AudioEncoder::src_event#event", {"transfer": "full", "$comment": "gstaudioencoder.c:1893-1897 - the same shape as the sink event above, with the default at :1867."}),
 ("GstAudio.AudioEncoder::handle_frame#buffer", {"nullable": True, "$comment": "gstaudioencoder.c:1186-1190 calls the slot and unrefs the buffer only if it is there, which is the drain calling the slot with NULL; gstaudioencoder.h:133 documents it. The gir carries no nullable."}),
 ("GstAudio.AudioEncoder::pre_push#buffer", {"direction": "inout", "transfer": "full", "$comment": "gstaudioencoder.c:1038-1048 - the same shape as the decoder above."}),
 ("GstVideo.VideoDecoder::sink_event#event", {"transfer": "full", "$comment": "gstvideodecoder.c:1716-1719 - the slot is called and nothing unrefs the event afterwards, and the default implementation at :1427 consumes it. The gir says none."}),
 ("GstVideo.VideoDecoder::src_event#event", {"transfer": "full", "$comment": "gstvideodecoder.c:1908-1911 - the same shape as the sink event above, with the default at :1795."}),
 ("GstVideo.VideoEncoder::sink_event#event", {"transfer": "full", "$comment": "gstvideoencoder.c:1324-1327 - the slot is called and nothing unrefs the event afterwards, and the default implementation at :1119 consumes it. The gir says none."}),
 ("GstVideo.VideoEncoder::src_event#event", {"transfer": "full", "$comment": "gstvideoencoder.c:1424-1427 - the same shape as the sink event above, with the default at :1330."}),
 ("GstVideo.VideoEncoder::handle_frame#frame", {"transfer": "full", "$comment": "gstvideoencoder.c:1608 makes the frame with one reference, :1723 lets the queue take one of its own, :1735 hands the original one to the slot, and the done: path at :1737-1740 never unrefs it; gst_video_encoder_finish_frame consumes it. The gir says none, where GstVideoDecoderClass::handle_frame already says full for the same shape."}),
]

anchor = '    "GstBase.BaseTransform::transform_caps#filter": '
index = next(i for i, line in enumerate(lines) if line.startswith(anchor))
lines[index + 1:index + 1] = [entry(k, v) for k, v in annotations]

# The allowlist, in the sorted order the array is kept in.
start = next(i for i, line in enumerate(lines) if line.startswith('  "subclassable": ['))
end = next(i for i in range(start, len(lines)) if lines[i].startswith('  ]'))
names = sorted(
    [line.strip().rstrip(',').strip('"') for line in lines[start + 1:end]]
    + ["GstBase.BaseParse", "GstAudio.AudioDecoder", "GstAudio.AudioEncoder",
       "GstVideo.VideoDecoder", "GstVideo.VideoEncoder"])
lines[start + 1:end] = ['    "' + name + '"' + (',' if i < len(names) - 1 else '')
                        for i, name in enumerate(names)]

# The comment above it no longer describes the split.
for i, line in enumerate(lines):
    if line.startswith('  "$comment-subclassable"'):
        lines[i] = line.split('Wave 1 is')[0] + (
            "Stage 2a is the seven Gst and GstBase classes and the seven GstAudio and GstVideo classes that are "
            "not codec bases; Stage 2b adds GstBase.BaseParse and the four codec bases, GstAudio.AudioDecoder, "
            "GstAudio.AudioEncoder, GstVideo.VideoDecoder and GstVideo.VideoEncoder.\",")
        break


def insert_sorted(section, additions):
    start = next(i for i, line in enumerate(lines) if line.startswith('  "' + section + '": {'))
    end = next(i for i in range(start, len(lines)) if lines[i].startswith('  }'))
    body = lines[start + 1:end]
    for key, value in additions:
        text = '    ' + json.dumps(key, ensure_ascii=False) + ': ' + json.dumps(value, ensure_ascii=False)
        position = end - start - 1
        for i, line in enumerate(body):
            if line.strip().split('": ')[0].strip('"') > key:
                position = i
                break

        body.insert(position, text + ',')

    body[-1] = body[-1].rstrip(',')
    for i in range(len(body) - 1):
        if not body[i].endswith(','):
            body[i] += ','

    lines[start + 1:end] = body


insert_sorted('vfuncDefaults', [
 ("GstAudio.AudioDecoder::getcaps", "Gst.Audio.AudioDecoderDefaults.ProxyGetcaps(dec, filter)"),
 ("GstAudio.AudioDecoder::pre_push", "Gst.FlowReturn.Ok"),
 ("GstAudio.AudioEncoder::getcaps", "Gst.Audio.AudioEncoderDefaults.ProxyGetcaps(enc, filter)"),
 ("GstAudio.AudioEncoder::pre_push", "Gst.FlowReturn.Ok"),
 ("GstBase.BaseParse::get_sink_caps",
  "{\n"
  "// gstbaseparse.c:1660-1688 - there is no function below the override to\n"
  "// reach: gst_base_parse_sink_query does the work inline when the slot is\n"
  "// NULL, answering the caps of the sink pad template intersected with the\n"
  "// filter, or those caps as they are when there is no filter.\n"
  "return Gst.Base.BaseParseDefaults.GetSinkCaps(parse, filter);\n}"),
 ("GstBase.BaseParse::pre_push_frame",
  "{\n"
  "// gstbaseparse.c:2606-2610 - the implementation below the override marks\n"
  "// the frame for clipping and answers Ok; it touches nothing else, which is\n"
  "// what makes it the no-op the base class installs.\n"
  "Gst.Base.BaseParseDefaults.MarkFrameForClipping(frame);\n"
  "return Gst.FlowReturn.Ok;\n}"),
 ("GstVideo.VideoDecoder::getcaps", "Gst.Video.VideoDecoderDefaults.ProxyGetcaps(decoder, filter)"),
 ("GstVideo.VideoEncoder::getcaps", "Gst.Video.VideoEncoderDefaults.ProxyGetcaps(enc, filter)"),
])

insert_sorted('vfuncNonNullReturns', [
 ("GstAudio.AudioEncoder::getcaps", "Gst.GstNative.CapsNewEmpty()"),
 ("GstBase.BaseParse::get_sink_caps", "Gst.GstNative.CapsNewEmpty()"),
])

insert_sorted('vfuncDocNotes', [
 ("GstAudio.AudioDecoder::handle_frame", "This slot has no implementation below it - the base class calls it unguarded - so a managed decoder has to declare it, which DefineSubclass checks for. A null buffer is the drain: the stream ended, and whatever is still held has to be pushed out."),
 ("GstAudio.AudioDecoder::parse", "The base class only calls this when the slot is set, guarding its one call site with if (klass->parse), so a chain-up reaches nothing and throws; an override implements the parsing rather than extending one."),
 ("GstAudio.AudioEncoder::handle_frame", "This slot has no implementation below it - the base class calls it unguarded - so a managed encoder has to declare it, which DefineSubclass checks for. A null buffer is the drain: the stream ended, and whatever is still held has to be pushed out."),
 ("GstBase.BaseParse::detect", "The base class only calls this when the slot is set - it reads priv->detecting = (klass->detect != NULL) once and skips the detection phase entirely otherwise - so a chain-up reaches nothing and throws; an override implements the detection rather than extending one."),
 ("GstBase.BaseParse::handle_frame", "This slot has no implementation below it - the base class calls it unguarded for every frame - so a managed parser has to declare it, which DefineSubclass checks for."),
 ("GstVideo.VideoDecoder::handle_frame", "This slot has no implementation below it - the base class calls it unguarded - so a managed decoder has to declare it, which DefineSubclass checks for. The override takes the frame over and hands it on with FinishFrame, DropFrame or ReleaseFrame."),
 ("GstVideo.VideoEncoder::handle_frame", "This slot has no implementation below it - the base class calls it unguarded - so a managed encoder has to declare it, which DefineSubclass checks for. The override takes the frame over and hands it on with FinishFrame, DropFrame or ReleaseFrame."),
])

io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
json.loads(io.open(p, encoding='utf-8').read())
print('written')
