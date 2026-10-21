import { Routes } from '@angular/router';
import { LoginComponent } from './features/auth/login/login.component';
import { TeacherDashboardComponent } from './features/teacher/teacher-dashboard.component';
import { StudentDashboardComponent } from './features/student/student-dashboard.component';

export const appRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'login',
  },
  {
    path: 'login',
    component: LoginComponent,
    title: 'SciKids LMS - Ingreso y Registro Seguro (Ley 1581 / Ley 1098)',
  },
  {
    path: 'auth/login',
    redirectTo: 'login',
  },
  {
    path: 'teacher/dashboard',
    component: TeacherDashboardComponent,
    title: 'SciKids LMS - Panel Docente & Semáforo Escolar',
  },
  {
    path: 'student/dashboard',
    component: StudentDashboardComponent,
    title: 'SciKids LMS - Mis Misiones Científicas (Primaria)',
  },
  {
    path: '**',
    redirectTo: 'login',
  },
];
