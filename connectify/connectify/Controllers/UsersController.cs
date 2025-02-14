﻿using connectify.Data;
using connectify.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ArticlesApp.Controllers
{
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext db;

        private readonly UserManager<ApplicationUser> _userManager;

        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager
            )
        {
            db = context;

            _userManager = userManager;

            _roleManager = roleManager;
        }
        public IActionResult Index(string search)
        {
            var users = from user in db.Users
                        orderby user.UserName
                        select user;
            if (User.IsInRole("Admin"))
            {
                ViewBag.UsersList = users;
            }


            if (search != null)
            {
                ViewBag.UsersList = users.Where(u => (u.FirstName.Contains(search) || u.LastName.Contains(search)));
            }

            //get current user friend list
            var currentUser = _userManager.GetUserAsync(User).Result;
            var friends = db.Friends.Where(f => f.User == currentUser && f.Status == "Accepted").Select(f => f.UserFriend).ToList();
            var aux = db.Friends.Where(f => f.UserFriend == currentUser && f.Status == "Accepted").Select(f => f.User).ToList();
            friends.AddRange(aux);
            ViewBag.isAdmin = User.IsInRole("Admin");
            ViewBag.friends = friends;
            ViewBag.currentUser = currentUser;
            return View();
        }

        public async Task<ActionResult> Show(string id)
        {
            ApplicationUser user = db.Users.Find(id);
            var roles = await _userManager.GetRolesAsync(user);

            ViewBag.Roles = roles;
            // only friends or admin can access show page or if user visibility is true
            var currentUser = _userManager.GetUserAsync(User).Result;
            var friends = db.Friends.Where(f => f.User == currentUser && f.Status == "Accepted").Select(f => f.UserFriend).ToList();
            var aux = db.Friends.Where(f => f.UserFriend == currentUser && f.Status == "Accepted").Select(f => f.User).ToList();
            friends.AddRange(aux);
            ViewBag.isAdmin = User.IsInRole("Admin");
            ViewBag.friends = friends;
            ViewBag.currentUser = currentUser;
            
            SetAccessRights();

            if (friends.Contains(user) || User.IsInRole("Admin") || user.Visibility == true)
            {
                return View(user);
            }
            else
            {
                return RedirectToAction("Index");
            }
        }

        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> Edit(string id)
        {
            ApplicationUser user = db.Users.Find(id);

            user.AllRoles = GetAllRoles();

            var roleNames = await _userManager.GetRolesAsync(user); // Lista de nume de roluri

            // Cautam ID-ul rolului in baza de date
            var currentUserRole = _roleManager.Roles
                                              .Where(r => roleNames.Contains(r.Name))
                                              .Select(r => r.Id)
                                              .First(); // Selectam 1 singur rol
            ViewBag.UserRole = currentUserRole;

            return View(user);
        }

        [HttpPost] 
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> Edit(string id, ApplicationUser newData, [FromForm] string newRole)
        {
            ApplicationUser user = db.Users.Find(id);

            user.AllRoles = GetAllRoles();


            if (ModelState.IsValid)
            {
                user.UserName = newData.UserName;
                user.Email = newData.Email;
                user.FirstName = newData.FirstName;
                user.LastName = newData.LastName;
                user.PhoneNumber = newData.PhoneNumber;


                // Cautam toate rolurile din baza de date
                var roles = db.Roles.ToList();

                foreach (var role in roles)
                {
                    // Scoatem userul din rolurile anterioare
                    await _userManager.RemoveFromRoleAsync(user, role.Name);
                }
                // Adaugam noul rol selectat
                var roleName = await _roleManager.FindByIdAsync(newRole);
                await _userManager.AddToRoleAsync(user, roleName.ToString());

                db.SaveChanges();

            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public IActionResult Delete(string id)
        {
            var user = db.Users
                         .Include("Posts")
                         .Include("Comments")
                         .Where(u => u.Id == id)
                         .First();

            // Delete user articles
            if (user.Posts.Count > 0)
            {
                foreach (connectify.Models.Post post in user.Posts)
                {
                    db.Posts.Remove(post);
                }
            }
            // Delete user comments
            if (user.Comments.Count > 0)
            {
                foreach (var comment in user.Comments)
                {
                    db.Comments.Remove(comment);
                }
            }

            db.ApplicationUsers.Remove(user);
            db.SaveChanges();

            return RedirectToAction("Index");
        }


        [NonAction]
        public IEnumerable<SelectListItem> GetAllRoles()
        {
            var selectList = new List<SelectListItem>();

            var roles = from role in db.Roles
                        select role;

            foreach (var role in roles)
            {
                selectList.Add(new SelectListItem
                {
                    Value = role.Id.ToString(),
                    Text = role.Name.ToString()
                });
            }
            return selectList;
        } 

        public async Task<IActionResult> AddFriend(string id)
        {
            ApplicationUser user = await _userManager.GetUserAsync(User);
            ApplicationUser userFriend = db.Users.Find(id);

            string userId = user.Id;
            
            if (userId == id)
            {
                return RedirectToAction("Index", "Posts");
            }

            //check if friend request already exists

            var fr = db.Friends.Where(f => f.UserId == user.Id && f.UserFriendId == userFriend.Id).FirstOrDefault();
            if (fr != null)
            {
                return RedirectToAction("Index", "Posts");
            }

            fr = db.Friends.Where(f => f.UserFriendId == user.Id && f.UserId == userFriend.Id).FirstOrDefault();
            
            if (fr != null)
            {
                return RedirectToAction("Index", "Posts");
            }
            Friend friendRequest = new Friend();
            friendRequest.UserId = userId;
            friendRequest.UserFriendId = id;
            friendRequest.Status = "Pending";
            friendRequest.UserFriend = userFriend;
            friendRequest.User = user;
            db.Friends.Add(friendRequest);
            db.SaveChanges();
            return RedirectToAction("Index", "Posts");

        }

        // remove friend

        public async Task<IActionResult> RemoveFriend(string id)
        {
            ApplicationUser user = await _userManager.GetUserAsync(User);
            ApplicationUser userFriend = db.Users.Find(id);

            string userId = user.Id;

            if (userId == id)
            {
                return RedirectToAction("Index", "Posts");
            }

            var fr = db.Friends.Where(f => f.UserId == user.Id && f.UserFriendId == userFriend.Id && f.Status == "Accepted").FirstOrDefault();
            if (fr != null)
            {
                db.Friends.Remove(fr);
                db.SaveChanges();
                return RedirectToAction("Index", "Posts");
            }

            fr = db.Friends.Where(f => f.UserFriendId == user.Id && f.UserId == userFriend.Id && f.Status == "Accepted").FirstOrDefault();

            if (fr != null)
            {
                db.Friends.Remove(fr);
                db.SaveChanges();
                return RedirectToAction("Index", "Posts");
            }
            return RedirectToAction("Index", "Posts");

        }

        [NonAction]
        private void SetAccessRights()
        {
            ViewBag.AfisareButoane = false;

            ViewBag.EsteAdmin = User.IsInRole("Admin");
        }
    }
}
