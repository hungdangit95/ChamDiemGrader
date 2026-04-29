namespace ChamDiemGrader.Services;

/// <summary>
/// Tiêu chí chấm điểm cố định theo thể lệ cuộc thi — nhúng thẳng vào code, không cần file ngoài.
/// </summary>
public static class EmbeddedCriteria
{
    public const string Text = """
TIÊU CHÍ CHẤM ĐIỂM CUỘC THI VIẾT "BỮA CƠM GIA ĐÌNH ẤM ÁP YÊU THƯƠNG"

=== BƯỚC 1: LOẠI BÀI KHÔNG HỢP LỆ ===
Bài dự thi bị loại (không đưa vào chấm điểm) nếu thuộc một trong các trường hợp sau:
1. Bài dự thi viết bằng tiếng nước ngoài.
2. Bài dự thi thể hiện dưới các hình thức khác (không phải văn xuôi) như: thơ ca, tranh ảnh, video, file âm nhạc...
3. Bài viết vượt quá 1.500 từ — không kể: tiêu đề; thông tin cá nhân (họ và tên, năm sinh/ngày/tháng, chức vụ/lớp, địa chỉ/đơn vị/liên hệ trường, số điện thoại, thời gian, địa điểm); Phiếu đăng ký dự thi.
4. Bài viết không phù hợp với văn hóa, thuần phong, mỹ tục và không tuân theo quy định của pháp luật của Việt Nam.
5. Bài dự thi không đúng chủ đề "Bữa cơm gia đình ấm áp yêu thương".
6. Bài dự thi là bài tập thể (nhóm tác giả).
7. Bài dự thi không được trình bày trên 1 mặt giấy A4 (đối với bài dự thi viết tay) và không được trình bày nội dung vào file văn bản, không đúng cỡ chữ 14 (đối với bài dự thi đánh máy).

Lưu ý quan trọng: Những bài bị mất điểm đáng kể ở phần Hình thức do không phân tách rõ "Thông điệp" (không quá 30 từ) hoặc có ràng ở cuối bài theo đúng yêu cầu kết thúc của mẫu thông điệp. Những bài xuất sắc đạt điểm cao là những bài có câu chuyện xúc động mạnh, trình bày thuyết phục và có thông điệp rõ ràng, súc tích, truyền thống.

=== BƯỚC 2: CHẤM ĐIỂM BÀI DỰ THI "HỢP LỆ" THEO THANG ĐIỂM ===

I. NỘI DUNG (Tối đa 6 điểm)

I.1 — Bám sát chủ đề (Tối đa 2 điểm)
Bài viết làm rõ 1 hoặc nhiều/đủ 1 trong 5 nhóm nội dung tại thể lệ:
  - Kỷ niệm khó quên: nội dung viết về bữa cơm gần liên, gợi nhớ một buổi ngoài, miêu tả hoặc một bước cảnh yêu của gia đình.
  - Hương vị quê nhà: nội dung viết về bữa cơm với những món ăn bình dị từ bà, mẹ, cha và ý nghĩa tinh thần đằng sau những bữa cơm gia đình.
  - Nội dung viết về bữa cơm giúp giải mọi thuận hoặc gắn kết các thế hệ (ông bà - cha mẹ - con cái).
  - Giáo dục gia đình từ mâm cơm: nội dung viết về bữa cơm và những câu chuyện trong cuộc sống hàng ngày — "Học ăn, học nói, học gói, học mở", giáo dục về lòng biết ơn, sự tình tế (nhận mặt, lòng hướng) và sự gắn kết của quá trình món ăn mục giáo dục gia đình từ mâm cơm mẫu giáo.
  - Góc nhìn hiện đại: nội dung viết về bữa cơm hiện nay — "bữa cơm online", bữa cơm ngày Tết hay việc duy trì thói quen ăn cùng nhau trong thời đại số.

I.2 — Bài viết có câu chuyện: kỷ niệm cụ thể, sâu sắc, thuyết phục (Tối đa 1 điểm)

I.3 — Nội dung xúc động, chân thực; truyền cảm hứng; tác động tích cực đến cộng đồng (Tối đa 1,5 điểm)

I.4 — Thể hiện giá trị gia đình qua bữa cơm (gắn kết, giáo dục, yêu thương, chia sẻ...) (Tối đa 1 điểm)

I.5 — Có ý nghĩa, có rễ: ý nghĩa tích cực, rút ra được thông điệp hoặc bài học về giá trị bữa cơm gia đình (Tối đa 0,5 điểm)

II. HÌNH THỨC (Tối đa 4 điểm)

II.1 — Câu văn mạch lạc, chân thật, ngắn gọn, dễ hiểu; ít lỗi chính tả (Tối đa 1,5 điểm)

II.2 — Văn phong mượt mà, có nhiều hình ảnh, đẹp, cảm xúc; sử dụng các biện pháp tu từ, nghệ thuật hợp lý, hiệu quả (Tối đa 1 điểm)

II.3 — Có bố cục hợp lý, rõ ràng (Tối đa 0,5 điểm)

II.4 — Có thông điệp rõ ràng KHÔNG QUÁ 30 TỪ (Tối đa 1 điểm)
Quy tắc đếm: từ tính theo khoảng trắng trong tiếng Việt.
Nếu không có phần thông điệp hoặc thông điệp vượt quá 30 từ thì điểm hạng mục này = 0.

III. TIÊU CHÍ PHỤ (0 điểm — chỉ dùng để xử lý đồng điểm)
- Đối với bài dự thi viết tay: Trình bày viết sáng, sạch đẹp.
- Có video, hình ảnh minh họa phù hợp với nội dung, thể lệ.
""";
}
