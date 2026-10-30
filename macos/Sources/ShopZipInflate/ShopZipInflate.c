#include "ShopZipInflate.h"
#include <limits.h>
#include <string.h>
#include <zlib.h>
int shop_inflate_raw(const uint8_t *input, size_t input_size, uint8_t *output, size_t output_size) {
    if (!input || !output || !output_size || input_size > UINT_MAX || output_size > UINT_MAX) return 0;
    z_stream stream;
    memset(&stream, 0, sizeof(stream));
    stream.next_in = (Bytef *)input; stream.avail_in = (uInt)input_size;
    stream.next_out = output; stream.avail_out = (uInt)output_size;
    if (inflateInit2(&stream, -MAX_WBITS) != Z_OK) return 0;
    int result = inflate(&stream, Z_FINISH);
    int valid = result == Z_STREAM_END && stream.total_in == input_size && stream.total_out == output_size;
    inflateEnd(&stream);
    return valid;
}
